using AskMyData.Application.Ask;
using AskMyData.Domain.Results;
using Microsoft.AspNetCore.Http;

namespace AskMyData.Api.Mapping;

/// <summary>
/// Mapped HTTP result: status code and an optional body. <see cref="WritesResponse"/> is false only for a
/// client abort, where nothing is written and the status exists for logging only.
/// </summary>
public sealed record MappedResponse(int StatusCode, object? Body)
{
    public bool WritesResponse => Body is not null;
}

/// <summary>Maps Orchestrator outcomes to HTTP status codes and bodies per the design Error Handling table.</summary>
public sealed class OutcomeHttpMapper(ILogger<OutcomeHttpMapper> logger)
{
    public const int ClientClosedRequestStatus = 499;

    public const string LlmUnavailableMessage = "The language model is unavailable.";
    public const string TimeoutMessage = "The request timed out.";
    public const string ExecutionErrorMessage = "The query could not be executed.";

    public MappedResponse Map(AskOutcome outcome, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome)
        {
            case AskSucceeded s:
                return new MappedResponse(StatusCodes.Status200OK, ToResponse(s));

            case AskValidationFailed v:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The generated query was rejected.", outcome, correlationId, p =>
                {
                    p.ReasonCode = v.ReasonCode;
                    p.Detail = v.Detail;
                    p.AttemptedSql = v.AttemptedSql;
                });

            case AskRefused r:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The question was refused.", outcome, correlationId, p =>
                {
                    p.ReasonCode = ReasonCodes.Refused;
                    p.Refusal = r.RefusalText;
                });

            case AskGenerationInvalid:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The model did not produce a valid answer.", outcome, correlationId, p =>
                    p.ReasonCode = ReasonCodes.GenerationInvalid);

            case AskLlmUnavailable l:
                return Problem(StatusCodes.Status503ServiceUnavailable, LlmUnavailableMessage, outcome, correlationId, p =>
                {
                    p.ReasonCode = l.TimedOut ? "LLM_TIMEOUT" : "LLM_UNAVAILABLE";
                    p.Detail = LlmUnavailableMessage;
                });

            case AskExecutionFailed { Category: ExecutionErrorCategory.Timeout }:
                return Problem(StatusCodes.Status504GatewayTimeout, TimeoutMessage, outcome, correlationId, p =>
                {
                    p.ReasonCode = WireFormat.Timeout;
                    p.Detail = TimeoutMessage;
                });

            case AskExecutionFailed:
                return Problem(StatusCodes.Status502BadGateway, ExecutionErrorMessage, outcome, correlationId, p =>
                {
                    p.ReasonCode = WireFormat.ExecutionError;
                    p.Detail = ExecutionErrorMessage;
                });

            case AskDeadlineExceeded:
                return Problem(StatusCodes.Status504GatewayTimeout, TimeoutMessage, outcome, correlationId, p =>
                {
                    p.ReasonCode = "DEADLINE_EXCEEDED";
                    p.Detail = TimeoutMessage;
                });

            case AskConversationNotFound n:
                return Problem(StatusCodes.Status404NotFound, "Conversation not found.", outcome, correlationId, p =>
                    p.Detail = $"Conversation '{n.RequestedId}' was not found or has expired.");

            case AskClientAborted:
                logger.LogInformation(
                    "Client aborted request; no response written. Status {StatusCode}, CorrelationId {CorrelationId}",
                    ClientClosedRequestStatus,
                    correlationId);
                return new MappedResponse(ClientClosedRequestStatus, null);

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.GetType().Name, "Unmapped outcome.");
        }
    }

    public static UsageDto ToDto(UsageSummary usage) =>
        new(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, usage.EstimatedCostUsd, usage.ElapsedMs);

    private static QuestionResponseDto ToResponse(AskSucceeded s) =>
        new(
            s.ConversationId,
            s.Sql,
            new ResultTableDto(
                s.Result.Columns.Select(c => new ResultColumnDto(c.Name, c.SqlType)).ToList(),
                s.Result.Rows),
            s.Result.Truncated,
            new ChartSuggestionDto(s.Chart.Type.ToWireString(), s.Chart.XColumn, s.Chart.YColumns),
            s.Explanation,
            s.MetricUsage,
            s.Warnings,
            ToDto(s.Usage));

    private static MappedResponse Problem(
        int status,
        string title,
        AskOutcome outcome,
        string correlationId,
        Action<AskProblemDetails> configure)
    {
        var problem = new AskProblemDetails
        {
            Status = status,
            Title = title,
            ConversationId = outcome.ConversationId,
            CorrelationId = correlationId,
            Usage = outcome is AskConversationNotFound ? null : ToDto(outcome.Usage),
        };
        configure(problem);
        return new MappedResponse(status, problem);
    }
}
