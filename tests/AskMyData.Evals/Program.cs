using AskMyData.Application.Execution;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using AskMyData.Evals.Runner;
using AskMyData.Infrastructure.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Eval_Suite console runner.
//   dotnet run --project tests/AskMyData.Evals -- --mode stub --out artifacts/evals [--connection <cs>] [--cases <dir>]
// Live_Mode is added in a later task (20.x); generate-expected is handled by GenerateExpectedCommand.
const string Usage = "Usage: --mode stub --out <dir> [--connection <read-only connection string>] [--cases <dir>]";

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

if (options.Mode != EvalMode.Stub)
{
    Console.Error.WriteLine("Live mode is not available yet.");
    return 2;
}

try
{
    var root = DatasetContainer.FindRepositoryRoot();
    var casesDirectory = options.CasesDirectory
        ?? Path.Combine(root ?? Directory.GetCurrentDirectory(), "tests", "AskMyData.Evals", "cases");
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
    var runner = new EvalRunner(
        options.Mode, cases, EvalHostFactory.CreateStub(executor, TimeProvider.System), TimeProvider.System);

    var report = await runner.RunAsync(cts.Token);
    await new ReportWriter().WriteAsync(report, options.OutDirectory, cts.Token);

    Console.WriteLine($"{report.Label}");
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
            default:
                return null;
        }
    }

    return mode is null || outDirectory is null ? null : new RunOptions(mode.Value, outDirectory, connection, cases);
}

internal sealed record RunOptions(EvalMode Mode, string OutDirectory, string? Connection, string? CasesDirectory);
