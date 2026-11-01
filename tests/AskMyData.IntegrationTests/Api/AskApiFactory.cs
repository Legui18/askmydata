using AskMyData.Application.Conversations;
using AskMyData.Domain.Conversations;
using AskMyData.Infrastructure.Conversations;
using AskMyData.Infrastructure.Llm;
using AskMyData.IntegrationTests.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.IntegrationTests.Api;

/// <summary>Questions and scripts used by the Api integration tests (Stub provider, no network).</summary>
public static class ApiScripts
{
    public const string RevenueByRegion = "What was total revenue by region in 2024?";
    public const string WestOnly = "Only for the West region";
    public const string NoRows = "What was total revenue by region in 1999?";

    private const string NoRowsSql =
        "SELECT s.Region AS Region, SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS TotalRevenue " +
        "FROM dbo.OrderLines AS ol JOIN dbo.Orders AS o ON o.OrderId = ol.OrderId " +
        "JOIN dbo.Stores AS s ON s.StoreId = o.StoreId WHERE YEAR(o.OrderDate) = 1999 " +
        "GROUP BY s.Region ORDER BY s.Region";

    /// <summary>Wraps the shipped script file and adds one question whose query yields zero rows.</summary>
    public sealed class WithZeroRowQuestion(IStubScriptSource inner) : IStubScriptSource
    {
        private readonly InMemoryStubScriptSource _extra = new InMemoryStubScriptSource().Add(
            NoRows,
            "{\"sql\":\"" + NoRowsSql + "\",\"metricUsage\":[\"TotalRevenue\"],"
            + "\"explanation\":\"Total revenue for 1999 by region.\",\"adHocCalculation\":false}");

        public bool TryGetScript(string question, out IReadOnlyList<string> responses) =>
            _extra.TryGetScript(question, out responses) || inner.TryGetScript(question, out responses);
    }
}

/// <summary>Counts Conversation creations so tests can assert that a 404 creates none.</summary>
public sealed class CountingConversationStore(IConversationStore inner) : IConversationStore
{
    private int _created;

    public int CreatedCount => Volatile.Read(ref _created);

    public async Task<ConversationId> CreateAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _created);
        return await inner.CreateAsync(ct);
    }

    public Task<IReadOnlyList<Turn>?> LoadRecentTurnsAsync(ConversationId id, int count, CancellationToken ct) =>
        inner.LoadRecentTurnsAsync(id, count, ct);

    public Task<bool> AppendTurnAsync(ConversationId id, string question, string sql, string explanation, CancellationToken ct) =>
        inner.AppendTurnAsync(id, question, sql, explanation, ct);
}

/// <summary>
/// Hosts the Api in-process with the Stub provider, the Testcontainers Dataset as Read_Only_User, a
/// <see cref="FakeTimeProvider"/> and a counting Conversation store.
/// </summary>
public sealed class AskApiFactory(string? connectionString, string provider = "Stub") : WebApplicationFactory<Program>
{
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    public CountingConversationStore Store { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Llm:Provider", provider);
        builder.UseSetting("ConnectionStrings:ReadOnly", connectionString ?? "Server=localhost;Database=AskMyData");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            services.RemoveAll<IStubScriptSource>();
            services.AddSingleton<IStubScriptSource>(sp =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "stub", "stub-scripts.json");
                return new ApiScripts.WithZeroRowQuestion(new FileStubScriptSource(path));
            });

            services.RemoveAll<IConversationStore>();
            services.AddSingleton(sp =>
                new CountingConversationStore(sp.GetRequiredService<InMemoryConversationStore>()));
            services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<CountingConversationStore>());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        Store = host.Services.GetRequiredService<CountingConversationStore>();
        return host;
    }
}

/// <summary>Shared Dataset container, loaded once for all Api tests.</summary>
public sealed class ApiDatasetFixture : IAsyncLifetime
{
    private readonly DatasetSqlServer _server = new();

    public string ConnectionString => _server.ReadOnlyConnectionString;

    public async Task InitializeAsync()
    {
        await _server.InitializeAsync();
        var run = await _server.RunScriptAsync();
        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException("Dataset_Script failed: " + run.Output);
        }
    }

    public Task DisposeAsync() => _server.DisposeAsync();
}
