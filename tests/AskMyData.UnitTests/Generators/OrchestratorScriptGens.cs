using AskMyData.Domain.Results;
using AskMyData.UnitTests.Orchestrator;
using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

/// <summary>Generates fake-clock delays for scripted LLM and executor steps.</summary>
public static class DelayScriptGen
{
    /// <summary>A delay between 0 and 90 s: spans below and above LLM_Timeout (30 s) and Request_Deadline (60 s).</summary>
    public static Gen<TimeSpan> Delay { get; } =
        Gen.Frequency(
            (3, Gen.Constant(TimeSpan.Zero)),
            (4, Gen.Choose(1, 29).Select(s => TimeSpan.FromSeconds(s))),
            (2, Gen.Choose(30, 59).Select(s => TimeSpan.FromSeconds(s))),
            (1, Gen.Choose(60, 90).Select(s => TimeSpan.FromSeconds(s))));

    /// <summary>A delay that never reaches LLM_Timeout.</summary>
    public static Gen<TimeSpan> Short { get; } = Gen.Choose(0, 5).Select(s => TimeSpan.FromSeconds(s));

    /// <summary>One delay per step.</summary>
    public static Gen<IReadOnlyList<TimeSpan>> Script(int count) =>
        GenHelpers.Sequence(Enumerable.Range(0, count).Select(_ => Delay));
}

/// <summary>Generates LLM scripts (one initial step and, usually, one repair step).</summary>
public static class LlmScriptGen
{
    public static Gen<string> ExplanationText { get; } =
        Gen.Elements("Counts the orders.", "Revenue by region.", "x", new string('e', 700));

    /// <summary>Output that parses and passes the validator.</summary>
    public static Gen<LlmStep> ValidStep { get; } =
        from explanation in ExplanationText
        from adHoc in Gen.Elements(true, false)
        select LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql, explanation: explanation, adHoc: adHoc));

    /// <summary>Output that parses but is rejected by the validator.</summary>
    public static Gen<LlmStep> InvalidSqlStep { get; } =
        Gen.Constant(LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql)));

    /// <summary>Output that is not a Structured_Response.</summary>
    public static Gen<LlmStep> GarbageStep { get; } = Gen.Constant(LlmStep.Respond(LlmResponses.Garbage));

    /// <summary>A refusal with a non-empty message.</summary>
    public static Gen<LlmStep> RefusalStep { get; } =
        Gen.Elements("I cannot modify data.", "That data is not available.")
            .Select(text => LlmStep.Respond(LlmResponses.Refusal(text)));

    /// <summary>A provider failure.</summary>
    public static Gen<LlmStep> UnavailableStep { get; } = Gen.Constant(LlmStep.Fail());

    /// <summary>Any step kind, weighted toward outputs that exercise validation and repair.</summary>
    public static Gen<LlmStep> Step { get; } =
        Gen.Frequency(
            (4, ValidStep),
            (3, InvalidSqlStep),
            (2, GarbageStep),
            (1, RefusalStep),
            (1, UnavailableStep));

    /// <summary>A script of one or two steps with delays from <see cref="DelayScriptGen.Short"/>.</summary>
    public static Gen<IReadOnlyList<LlmStep>> Script { get; } = WithDelays(DelayScriptGen.Short);

    /// <summary>A script of one or two steps whose delays come from <paramref name="delays"/>.</summary>
    public static Gen<IReadOnlyList<LlmStep>> WithDelays(Gen<TimeSpan> delays) =>
        from count in Gen.Choose(1, 2)
        from steps in GenHelpers.Sequence(Enumerable.Range(0, count).Select(_ =>
            from step in Step
            from delay in delays
            select step.WithDelay(delay)))
        select steps;
}

/// <summary>Generates executor scripts: row sets with marker strings, TIMEOUT and EXECUTION_ERROR.</summary>
public static class ExecutorScriptGen
{
    public static Gen<ExecutorStep> RowsStep { get; } =
        from count in Gen.Choose(0, 12)
        from truncated in Gen.Elements(true, false)
        from seed in Gen.Choose(0, 999)
        select ExecutorStep.Rows(Results.Sample(count, truncated, seed));

    public static Gen<ExecutorStep> TimeoutStep { get; } =
        Gen.Constant(ExecutorStep.Fail(ExecutionErrorCategory.Timeout));

    public static Gen<ExecutorStep> ErrorStep { get; } =
        Gen.Constant(ExecutorStep.Fail(ExecutionErrorCategory.ExecutionError));

    public static Gen<ExecutorStep> Step { get; } =
        Gen.Frequency((5, RowsStep), (2, TimeoutStep), (2, ErrorStep));

    public static Gen<IReadOnlyList<ExecutorStep>> Script { get; } = WithDelays(DelayScriptGen.Short);

    public static Gen<IReadOnlyList<ExecutorStep>> WithDelays(Gen<TimeSpan> delays) =>
        from count in Gen.Choose(1, 2)
        from steps in GenHelpers.Sequence(Enumerable.Range(0, count).Select(_ =>
            from step in Step
            from delay in delays
            select step.WithDelay(delay)))
        select steps;
}
