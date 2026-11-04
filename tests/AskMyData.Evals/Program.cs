using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using AskMyData.Evals.Runner;
using AskMyData.Infrastructure.Execution;
using AskMyData.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Eval_Suite console runner.
//   dotnet run --project tests/AskMyData.Evals -- --mode stub --out artifacts/evals [--connection <cs>] [--cases <dir>]
//   dotnet run --project tests/AskMyData.Evals -- --mode live --out artifacts/evals-live [--delay-ms <n>]   (needs Llm__Gemini__ApiKey)
// --delay-ms pauses between cases (default: 0 in stub mode, 6000 in live mode) to respect provider rate limits.
// generate-expected is handled by GenerateExpectedCommand.
const string Usage = "Usage: --mode stub|live --out <dir> [--connection <read-only connection string>] [--cases <dir>] [--delay-ms <n>]";
const int DefaultLiveDelayMs = 6000;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (args.Length > 0 && args[0] == GenerateExpectedCommand.Name)
{
    try
    {
        return await GenerateExpectedCommand.RunAsync(args[1..], cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Cancelled.");
        return 130;
    }
    catch (EvalCaseFormatException ex)
    {
        Console.Error.WriteLine($"Invalid eval cases: {ex.Message}");
        return 2;
    }
}

var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

LlmOptions? llmOptions = null;
if (options.Mode == EvalMode.Live)
{
    llmOptions = LiveLlmFactory.LoadOptions(Environment.GetEnvironmentVariable);
    if (llmOptions is null)
    {
        Console.Error.WriteLine("Live mode needs an API key: set Llm__Gemini__ApiKey (or GEMINI_API_KEY).");
        return 2;
    }
}

try
{
    var root = DatasetContainer.FindRepositoryRoot();
    var casesDirectory = options.CasesDirectory
        ?? Path.Combine(root ?? Directory.GetCurrentDirectory(), "tests", "AskMyData.Evals", "Cases");
    var cases = await EvalCaseLoader.LoadDirectoryAsync(casesDirectory, cts.Token);

    await using var container = new DatasetContainer();
    var connectionString = options.Connection;
    if (connectionString is null)
    {
        if (root is null)
        {
            Console.Error.WriteLine("Could not locate the repository root (AskMyData.sln) to find db/dataset.sql.");
            return 2;
        }

        Console.WriteLine("Starting SQL Server 2022 container and loading db/dataset.sql ...");
        await container.StartAsync(Path.Combine(root, "db", "dataset.sql"), cts.Token);
        connectionString = container.ReadOnlyConnectionString;
    }

    var executor = new SqlServerQueryExecutor(
        Options.Create(new QueryExecutorOptions { ConnectionString = connectionString }),
        NullLogger<SqlServerQueryExecutor>.Instance,
        TimeProvider.System);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    LlmUsageRecorder? recorder = null;
    Func<EvalCase, EvalHost> hostFactory;
    if (llmOptions is null)
    {
        hostFactory = EvalHostFactory.CreateStub(executor, TimeProvider.System);
    }
    else
    {
        // Live_Mode: the Gemini adapter is reached only through ILlmClient; the decorator records usage.
        recorder = new LlmUsageRecorder(llmOptions.Gemini.Model);
        ILlmClient llm = new RecordingLlmClient(LiveLlmFactory.CreateGemini(http, llmOptions), recorder);
        hostFactory = EvalHostFactory.CreateLive(executor, TimeProvider.System, llm);
    }

    var delayMs = options.DelayMs ?? (options.Mode == EvalMode.Live ? DefaultLiveDelayMs : 0);
    var runner = new EvalRunner(
        options.Mode, cases, hostFactory, TimeProvider.System, recorder, TimeSpan.FromMilliseconds(delayMs));

    if (delayMs > 0)
    {
        Console.WriteLine($"Pausing {delayMs} ms between cases. This run can take several minutes.");
    }

    var report = await runner.RunAsync(cts.Token);
    await new ReportWriter().WriteAsync(report, options.OutDirectory, cts.Token);

    Console.WriteLine($"{report.Label}");
    if (report.Model is not null)
    {
        Console.WriteLine($"Model: {report.Model}");
    }

    if (report.TokenUsage is not null)
    {
        Console.WriteLine(
            $"Tokens: prompt {report.TokenUsage.PromptTokens}, completion {report.TokenUsage.CompletionTokens}, total {report.TokenUsage.TotalTokens}");
    }

    // Two separate numbers: accuracy on normal cases and safety on adversarial cases.
    var normalResults = report.Results.Where(r => r.Category != EvalCategories.Adversarial).ToList();
    var adversarialResults = report.Results.Where(r => r.Category == EvalCategories.Adversarial).ToList();
    Console.WriteLine($"Accuracy (normal cases): {normalResults.Count(r => r.Passed)}/{normalResults.Count}");
    Console.WriteLine($"Safety (adversarial cases): {adversarialResults.Count(r => r.Passed)}/{adversarialResults.Count}");

    Console.WriteLine($"Passed {report.Totals.Passed}/{report.Totals.Cases} ({report.Totals.PassPercentage}%). Reports in {options.OutDirectory}");
    foreach (var failed in report.Results.Where(r => !r.Passed))
    {
        Console.WriteLine($"FAIL {failed.CaseId}: {failed.Message}");
    }

    return EvalRunner.ExitCode(options.Mode, report);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (EvalCaseFormatException ex)
{
    Console.Error.WriteLine($"Invalid eval cases: {ex.Message}");
    return 2;
}

static RunOptions? ParseArgs(string[] args)
{
    EvalMode? mode = null;
    string? outDirectory = null;
    string? connection = null;
    string? cases = null;
    int? delayMs = null;

    for (var i = 0; i < args.Length; i++)
    {
        var name = args[i];
        if (i + 1 >= args.Length)
        {
            return null;
        }

        var value = args[++i];
        switch (name)
        {
            case "--mode":
                mode = value.ToLowerInvariant() switch
                {
                    "stub" => EvalMode.Stub,
                    "live" => EvalMode.Live,
                    _ => null,
                };
                if (mode is null)
                {
                    return null;
                }

                break;
            case "--out":
                outDirectory = value;
                break;
            case "--connection":
                connection = value;
                break;
            case "--cases":
                cases = value;
                break;
            case "--delay-ms":
                if (!int.TryParse(value, out var parsedDelay) || parsedDelay < 0)
                {
                    return null;
                }

                delayMs = parsedDelay;
                break;
            default:
                return null;
        }
    }

    return mode is null || outDirectory is null ? null : new RunOptions(mode.Value, outDirectory, connection, cases, delayMs);
}

internal sealed record RunOptions(EvalMode Mode, string OutDirectory, string? Connection, string? CasesDirectory, int? DelayMs);