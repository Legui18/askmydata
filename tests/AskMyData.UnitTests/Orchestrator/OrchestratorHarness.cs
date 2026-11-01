using System.Text.Json;
using AskMyData.Application.Ask;
using AskMyData.Application.Charts;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using AskMyData.Application.Options;
using AskMyData.Application.Prompting;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Conversations;
using AskMyData.Infrastructure.Validation;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Builds Structured_Response JSON the way an LLM would return it.</summary>
public static class LlmResponses
{
    /// <summary>SQL that passes every validator rule with no declared Metric.</summary>
    public const string ValidSql = "SELECT COUNT(*) AS N FROM dbo.Orders AS o";

    /// <summary>SQL that the validator rejects with SELECT_STAR.</summary>
    public const string InvalidSql = "SELECT * FROM dbo.Orders AS o";

    public static string Structured(
        string sql,
        IReadOnlyList<string>? metricUsage = null,
        string explanation = "Explanation.",
        bool adHoc = false,
        string? refusal = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sql"] = sql,
            ["metricUsage"] = metricUsage ?? [],
            ["explanation"] = explanation,
            ["adHocCalculation"] = adHoc,
            ["refusal"] = refusal,
        });

    public static string Refusal(string text) => Structured(string.Empty, refusal: text);

    /// <summary>Output the parser rejects as a Structured_Response.</summary>
    public const string Garbage = "this is not json";
}

/// <summary>Deterministic INonceSource for tests.</summary>
public sealed class CounterNonceSource : INonceSource
{
    private int _n;

    public string Next() => $"nonce{Interlocked.Increment(ref _n):D6}";
}

/// <summary>
/// Wires an <see cref="AskQuestionHandler"/> from the scripted doubles, the real <c>ScriptDomSqlValidator</c> behind
/// a <see cref="ValidatorSpy"/>, an <c>InMemoryConversationStore</c> and a <see cref="FakeTimeProvider"/>.
/// Reused by the orchestrator property tests.
/// </summary>
public sealed class OrchestratorHarness
{
    public const string DefaultQuestion = "How many orders are there?";

    private OrchestratorHarness(
        FakeTimeProvider time,
        ScriptedLlmClient llm,
        ScriptedQueryExecutor executor,
        ValidatorSpy validator,
        CapturingAuditLog audit,
        InMemoryConversationStore store,
        AskOptions options,
        PromptBuilder prompts,
        AskQuestionHandler handler)
    {
        Time = time;
        Llm = llm;
        Executor = executor;
        Validator = validator;
        Audit = audit;
        Store = store;
        Options = options;
        Prompts = prompts;
        Handler = handler;
    }

    public FakeTimeProvider Time { get; }

    public ScriptedLlmClient Llm { get; }

    public ScriptedQueryExecutor Executor { get; }

    public ValidatorSpy Validator { get; }

    public CapturingAuditLog Audit { get; }

    public InMemoryConversationStore Store { get; }

    public AskOptions Options { get; }

    public PromptBuilder Prompts { get; }

    public AskQuestionHandler Handler { get; }

    public static OrchestratorHarness Create(
        IReadOnlyList<LlmStep> llmScript,
        IReadOnlyList<ExecutorStep>? executorScript = null,
        AskOptions? options = null,
        LlmPricingOptions? pricing = null)
    {
        options ??= new AskOptions();
        var time = new FakeTimeProvider();
        var layer = SemanticLayerCatalog.Create();
        var llm = new ScriptedLlmClient(llmScript, time);
        var executor = new ScriptedQueryExecutor(executorScript ?? [ExecutorStep.Rows(Results.Sample(1))], time);
        var validator = new ValidatorSpy(new ScriptDomSqlValidator(layer));
        var audit = new CapturingAuditLog();
        var store = new InMemoryConversationStore(Microsoft.Extensions.Options.Options.Create(new ConversationStoreOptions()), time);
        var prompts = new PromptBuilder(layer, new CounterNonceSource());
        var handler = new AskQuestionHandler(
            llm,
            validator,
            executor,
            store,
            audit,
            prompts,
            new StructuredResponseParser(),
            new ChartSuggester(),
            Microsoft.Extensions.Options.Options.Create(options),
            time,
            new CostCalculator(Microsoft.Extensions.Options.Options.Create(pricing ?? new LlmPricingOptions())));
        return new OrchestratorHarness(time, llm, executor, validator, audit, store, options, prompts, handler);
    }

    /// <summary>Starts the request without awaiting it, so the caller can advance <see cref="Time"/>.</summary>
    public Task<AskOutcome> AskAsync(
        string question = DefaultQuestion,
        string? conversationId = null,
        string correlationId = "corr-1",
        CancellationToken requestAborted = default) =>
        Handler.HandleAsync(new AskCommand(question, conversationId, correlationId), requestAborted);

    /// <summary>
    /// Runs the request while stepping the fake clock in 1 s increments until it completes or
    /// <paramref name="maxAdvance"/> has elapsed.
    /// </summary>
    public async Task<AskOutcome> AskAndAdvanceAsync(
        TimeSpan maxAdvance,
        string question = DefaultQuestion,
        string? conversationId = null,
        CancellationToken requestAborted = default)
    {
        var task = AskAsync(question, conversationId, requestAborted: requestAborted);
        var step = TimeSpan.FromSeconds(1);
        for (var elapsed = TimeSpan.Zero; !task.IsCompleted && elapsed <= maxAdvance; elapsed += step)
        {
            Time.Advance(step);

            // Let continuations triggered by this tick finish on the real clock before the next tick, so a
            // cancellation is classified against the fake time at which it fired.
            await Task.WhenAny(task, Task.Delay(15)).ConfigureAwait(false);
        }

        return await task.ConfigureAwait(false);
    }
}

/// <summary>Builders for QueryResult values carrying recognizable marker strings.</summary>
public static class Results
{
    public const string MarkerPrefix = "ROWMARK-";

    public static QueryResult Sample(int rowCount, bool truncated = false, int seed = 0)
    {
        var columns = new[]
        {
            new ResultColumn("Region", "nvarchar", ColumnKind.Text),
            new ResultColumn("Total", "decimal", ColumnKind.Numeric),
        };
        var rows = Enumerable.Range(0, rowCount)
            .Select(i => new object?[] { $"{MarkerPrefix}{seed}-{i}", (decimal)(i + 1) })
            .ToArray();
        return new QueryResult(columns, rows, truncated, TimeSpan.FromMilliseconds(5));
    }
}
