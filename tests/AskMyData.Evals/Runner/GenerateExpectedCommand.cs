using AskMyData.Application.Execution;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Execution;
using AskMyData.Infrastructure.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AskMyData.Evals.Runner;

/// <summary>
/// <c>generate-expected [--connection &lt;cs&gt;] [--check] [--cases &lt;dir&gt;]</c>. Without <c>--connection</c> a
/// Testcontainers SQL Server loaded with <c>db/dataset.sql</c> is used. Exit codes: 0 ok, 1 diff (check) or failures, 2 usage.
/// </summary>
public static class GenerateExpectedCommand
{
    public const string Name = "generate-expected";
    private const string Usage = "Usage: generate-expected [--connection <read-only connection string>] [--check] [--cases <dir>]";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? connection = null;
        string? cases = null;
        var check = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check":
                    check = true;
                    break;
                case "--connection" when i + 1 < args.Length:
                    connection = args[++i];
                    break;
                case "--cases" when i + 1 < args.Length:
                    cases = args[++i];
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        var root = DatasetContainer.FindRepositoryRoot();
        var casesDirectory = cases
            ?? Path.Combine(root ?? Directory.GetCurrentDirectory(), "tests", "AskMyData.Evals", "Cases");

        await using var container = new DatasetContainer();
        if (connection is null)
        {
            if (root is null)
            {
                Console.Error.WriteLine("Could not locate the repository root (AskMyData.sln) to find db/dataset.sql.");
                return 2;
            }

            Console.WriteLine("Starting SQL Server 2022 container and loading db/dataset.sql ...");
            await container.StartAsync(Path.Combine(root, "db", "dataset.sql"), ct);
            connection = container.ReadOnlyConnectionString;
        }

        var executor = new SqlServerQueryExecutor(
            Options.Create(new QueryExecutorOptions { ConnectionString = connection }),
            NullLogger<SqlServerQueryExecutor>.Instance,
            TimeProvider.System);
        var generator = new ExpectedGenerator(executor, new ScriptDomSqlValidator(SemanticLayerCatalog.Create()));

        var result = await generator.GenerateAsync(casesDirectory, check, ct);

        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"ERROR {error}");
        }

        Console.WriteLine($"Processed {result.Processed} case(s), skipped {result.Skipped} adversarial case(s).");
        if (result.Errors.Count > 0)
        {
            return 1;
        }

        foreach (var change in result.Changed)
        {
            Console.WriteLine($"{(check ? "WOULD CHANGE" : "UPDATED")} {change}");
        }

        if (check && result.Changed.Count > 0)
        {
            Console.Error.WriteLine(
                $"{result.Changed.Count} case file(s) differ from regenerated expected rows. Run generate-expected and commit the result.");
            return 1;
        }

        return 0;
    }
}
