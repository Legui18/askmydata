using AskMyData.Application.Charts;
using AskMyData.Application.Conversations;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using AskMyData.Application.Options;
using AskMyData.Application.Prompting;
using AskMyData.Application.Validation;
using AskMyData.Domain.Conversations;
using AskMyData.Domain.Results;
using Microsoft.Extensions.Options;

namespace AskMyData.Application.Ask;

/// <summary>
/// The Orchestrator: conversation, prompt, one LLM call, validation, execution, at most one shared Repair_Call,
/// all inside one Request_Deadline (design "Orchestrator algorithm", KD2, KD3, KD5, KD7).
/// </summary>
public sealed class AskQuestionHandler(
    ILlmClient llm,
    ISqlValidator validator,
    IQueryExecutor executor,
    IConversationStore conversations,
    IAuditLog audit,
    PromptBuilder prompts,
    StructuredResponseParser parser,
    ChartSuggester charts,
    IOptions<AskOptions> options,
    TimeProvider time,
    CostCalculator? costs = null)
{
    /// <summary>Warning added when the answer is not based on a governed Metric (R2.9).</summary>
    public const string AdHocWarning = "This calculation is not a governed Metric.";

    private const string LlmUnavailableReason = "LLM_UNAVAILABLE";
    private const string LlmTimeoutReason = "LLM_TIMEOUT";
    private const string DeadlineExceededReason = "DEADLINE_EXCEEDED";
    private const string ConversationNotFoundReason = "CONVERSATION_NOT_FOUND";
    private const string ClientAbortedReason = "CLIENT_ABORTED";
    private const string UnhandledReason = "UNHANDLED";

    public async Task<AskOutcome> HandleAsync(AskCommand command, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(command);
        var opts = options.Value;

        // Step 1: Request_Deadline driven by TimeProvider, linked with the client's abort token.
        var start = time.GetTimestamp();
        using var deadlineCts = new CancellationTokenSource(opts.RequestDeadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, deadlineCts.Token);
        var usage = new UsageAccumulator();
        var state = new RequestState(command.ConversationId ?? string.Empty);

        TimeSpan Remaining()
        {
            var remaining = opts.RequestDeadline - time.GetElapsedTime(start);
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        bool DeadlineReached() => deadlineCts.IsCancellationRequested || Remaining() <= TimeSpan.Zero;
        long ElapsedMs() => (long)time.GetElapsedTime(start).TotalMilliseconds;
        UsageSummary Usage() => usage.Snapshot(ElapsedMs());

        AskOutcome Aborted() => Finish(state, new AskClientAborted(state.ConversationId, Usage()), ClientAbortedReason);
        AskOutcome Deadline() => Finish(state, new AskDeadlineExceeded(state.ConversationId, Usage()), DeadlineExceededReason);

        try
        {
            // Step 2: load history or create a Conversation. Unknown or unparsable ids stop before any LLM call.
            IReadOnlyList<Turn> history = [];
            ConversationId conversationId;
            if (command.ConversationId is not null)
            {
                if (!ConversationId.TryParse(command.ConversationId, out conversationId))
                {
                    return Finish(state, new AskConversationNotFound(command.ConversationId), ConversationNotFoundReason);
                }

                var turns = await conversations
                    .LoadRecentTurnsAsync(conversationId, opts.HistoryTurns, linked.Token)
                    .ConfigureAwait(false);
                if (turns is null)
                {
                    return Finish(state, new AskConversationNotFound(command.ConversationId), ConversationNotFoundReason);
                }

                history = turns;
            }
            else
            {
                conversationId = await conversations.CreateAsync(linked.Token).ConfigureAwait(false);
            }

            state.ConversationId = conversationId.Value;

            // Step 3: attempt is only ever 1 or 2, so at most one Repair_Call exists.
            RepairInfo? repair = null;
            string? failingSql = null;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var canRepair = attempt == 1;

                // Step 4: deadline check before every LLM call (also blocks a Repair_Call after expiry).
                if (DeadlineReached())
                {
                    return Deadline();
                }

                // Step 5.
                var request = prompts.Build(command.Question, history, failingSql, repair);

                // Step 6: LLM call bounded by min(LLM_Timeout, remaining).
                var remainingBefore = Remaining();
                var bound = remainingBefore < opts.LlmTimeout ? remainingBefore : opts.LlmTimeout;
                var boundIsDeadline = remainingBefore <= opts.LlmTimeout;
                using var timeoutCts = new CancellationTokenSource(bound, time);
                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeoutCts.Token);
                var callStart = time.GetTimestamp();

                LlmResponse response;
                try
                {
                    response = await llm.CompleteAsync(request, callCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (requestAborted.IsCancellationRequested)
                    {
                        return Aborted();
                    }

                    if (deadlineCts.IsCancellationRequested || (boundIsDeadline && timeoutCts.IsCancellationRequested))
                    {
                        return Deadline();
                    }

                    if (timeoutCts.IsCancellationRequested)
                    {
                        audit.LlmCallFailed(llm.ProviderName, request.CallType, time.GetElapsedTime(callStart), command.CorrelationId, true);
                        return Finish(state, new AskLlmUnavailable(state.ConversationId, Usage(), true), LlmTimeoutReason);
                    }

                    throw;
                }
                catch (LlmUnavailableException)
                {
                    audit.LlmCallFailed(llm.ProviderName, request.CallType, time.GetElapsedTime(callStart), command.CorrelationId, false);
                    return Finish(state, new AskLlmUnavailable(state.ConversationId, Usage(), false), LlmUnavailableReason);
                }

                state.Model = response.ModelName;
                usage.Add(
                    response,
                    costs?.Estimate(response.ModelName, response.PromptTokens, response.CompletionTokens) ?? 0m);

                // Step 7: parse. A non-empty refusal ends the request (KD3).
                var parsed = parser.TryParse(response.RawText);
                if (parsed is not null && !string.IsNullOrEmpty(parsed.Refusal))
                {
                    return Finish(state, new AskRefused(state.ConversationId, Usage(), parsed.Refusal), ReasonCodes.Refused);
                }

                if (parsed is null)
                {
                    if (canRepair)
                    {
                        state.RepairCallOccurred = true;
                        repair = new RepairInfo(ReasonCodes.GenerationInvalid, null);
                        failingSql = null;
                        continue;
                    }

                    return Finish(state, new AskGenerationInvalid(state.ConversationId, Usage()), ReasonCodes.GenerationInvalid);
                }

                state.Sql = parsed.Sql;

                // Step 8: no governed Metric means the calculation is ad hoc.
                var adHoc = parsed.AdHocCalculation || parsed.MetricUsage.Count == 0;

                // Step 9: validate before anything else.
                var validation = validator.Validate(parsed.Sql, parsed.MetricUsage);
                if (!validation.Passed)
                {
                    var reason = validation.ReasonCode ?? ReasonCodes.ParseError;
                    state.ValidationOutcome = AuditValidationOutcome.Rejected;
                    state.ValidationReasonCode = reason;
                    audit.ValidationFailed(reason, command.CorrelationId);

                    if (canRepair)
                    {
                        state.RepairCallOccurred = true;
                        repair = new RepairInfo(reason, validation.MetricName);
                        failingSql = parsed.Sql;
                        continue;
                    }

                    return Finish(
                        state,
                        new AskValidationFailed(state.ConversationId, Usage(), reason, validation.Detail, parsed.Sql),
                        reason);
                }

                state.ValidationOutcome = AuditValidationOutcome.Accepted;
                state.ValidationReasonCode = null;

                // Step 10: mint ValidatedSql only after a pass; execute within min(command timeout, remaining).
                var validated = new ValidatedSql(parsed.Sql);
                var remainingForSql = Remaining();
                var budget = new QueryBudget(remainingForSql < opts.CommandTimeout ? remainingForSql : opts.CommandTimeout);

                OneOf<QueryResult, ExecutionError> executed;
                var executionStart = time.GetTimestamp();
                try
                {
                    executed = await executor.ExecuteAsync(validated, budget, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (requestAborted.IsCancellationRequested)
                    {
                        return Aborted();
                    }

                    if (DeadlineReached())
                    {
                        return Deadline();
                    }

                    throw;
                }

                state.ExecutionMs += (long)time.GetElapsedTime(executionStart).TotalMilliseconds;

                if (executed.TryGetSecond(out var error))
                {
                    if (DeadlineReached())
                    {
                        return Deadline();
                    }

                    if (canRepair)
                    {
                        state.RepairCallOccurred = true;
                        repair = new RepairInfo(error.Category.ToWireString(), null);
                        failingSql = parsed.Sql;
                        continue;
                    }

                    return Finish(
                        state,
                        new AskExecutionFailed(state.ConversationId, Usage(), error.Category),
                        error.Category.ToWireString());
                }

                // Step 11: success.
                executed.TryGetFirst(out var result);
                var chart = charts.Suggest(result);
                var explanation = Truncate(parsed.Explanation, opts.MaxExplanationLength);
                var warnings = adHoc ? new[] { AdHocWarning } : [];
                await conversations
                    .AppendTurnAsync(conversationId, command.Question, parsed.Sql, explanation, CancellationToken.None)
                    .ConfigureAwait(false);

                state.RowCount = result.Rows.Count;
                state.Succeeded = true;
                return Finish(
                    state,
                    new AskSucceeded(state.ConversationId, Usage(), parsed.Sql, result, chart, explanation, parsed.MetricUsage, warnings),
                    null);
            }

            throw new InvalidOperationException("Unreachable: attempt is limited to 1 and 2.");
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            return Aborted();
        }
        catch (OperationCanceledException) when (deadlineCts.IsCancellationRequested)
        {
            return Deadline();
        }
        finally
        {
            // Step 12: exactly one audit record per request.
            var finalUsage = Usage();
            audit.Write(new AuditRecord(
                command.CorrelationId,
                state.ConversationId,
                command.Question,
                state.Sql,
                state.ValidationOutcome,
                state.ValidationReasonCode,
                state.Succeeded ? null : state.FailureReason ?? UnhandledReason,
                finalUsage.ElapsedMs,
                finalUsage.PromptTokens,
                finalUsage.CompletionTokens,
                finalUsage.EstimatedCostUsd,
                state.ExecutionMs,
                state.Succeeded ? state.RowCount : 0,
                state.RepairCallOccurred,
                llm.ProviderName,
                state.Model));
        }
    }

    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut];
    }

    private static AskOutcome Finish(RequestState state, AskOutcome outcome, string? failureReason)
    {
        state.FailureReason = failureReason;
        return outcome;
    }

    private sealed class RequestState(string conversationId)
    {
        public string ConversationId { get; set; } = conversationId;

        public string Sql { get; set; } = string.Empty;

        public AuditValidationOutcome ValidationOutcome { get; set; } = AuditValidationOutcome.NotValidated;

        public string? ValidationReasonCode { get; set; }

        public string? FailureReason { get; set; }

        public bool Succeeded { get; set; }

        public int RowCount { get; set; }

        public long ExecutionMs { get; set; }

        public bool RepairCallOccurred { get; set; }

        public string Model { get; set; } = string.Empty;
    }
}
