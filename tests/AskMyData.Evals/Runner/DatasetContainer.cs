using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AskMyData.Evals.Runner;

/// <summary>
/// Throw-away SQL Server 2022 container loaded with <c>db/dataset.sql</c>; exposes the Read_Only_User connection
/// string so Eval_Cases run with the same privileges as production.
/// </summary>
public sealed class DatasetContainer : IAsyncDisposable
{
    private const string ReadOnlyPassword = "R3adOnly!Passw0rd";
    private const string SqlcmdPath = "/opt/mssql-tools18/bin/sqlcmd";
    private const string ScriptPathInContainer = "/tmp/dataset.sql";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ReadOnlyConnectionString { get; private set; } = string.Empty;

    public async Task StartAsync(string datasetScriptPath, CancellationToken ct)
    {
        await _container.StartAsync(ct).ConfigureAwait(false);
        await _container.CopyAsync(await File.ReadAllBytesAsync(datasetScriptPath, ct).ConfigureAwait(false), ScriptPathInContainer, ct: ct)
            .ConfigureAwait(false);

        var result = await _container.ExecAsync(
            [
                SqlcmdPath, "-b", "-C", "-S", "localhost", "-U", MsSqlBuilder.DefaultUsername,
                "-P", MsSqlBuilder.DefaultPassword, "-i", ScriptPathInContainer,
                "-v", $"READONLY_PASSWORD={ReadOnlyPassword}",
            ],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"dataset.sql failed (exit {result.ExitCode}): {result.Stdout} {result.Stderr}");
        }

        ReadOnlyConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "AskMyData",
            UserID = "askmydata_reader",
            Password = ReadOnlyPassword,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>Walks up from the working directory and the app base to find the repository root (AskMyData.sln).</summary>
    public static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AskMyData.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
