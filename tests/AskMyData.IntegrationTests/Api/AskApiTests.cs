using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AskMyData.IntegrationTests.Api;

/// <summary>End-to-end tests of <c>POST /api/ask</c> with the Stub provider over the Testcontainers Dataset.</summary>
public sealed class AskApiTests(ApiDatasetFixture dataset) : IClassFixture<ApiDatasetFixture>
{
    private static async Task<(HttpStatusCode Status, JsonElement Body)> AskAsync(
        HttpClient client, string question, string? conversationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/ask", new { question, conversationId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body);
    }

    [Fact]
    public async Task ScriptedQuestion_Returns200WithSqlRowsAndNewConversation()
    {
        await using var factory = new AskApiFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();

        var (status, body) = await AskAsync(client, ApiScripts.RevenueByRegion);

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("sql").GetString().Should().Contain("dbo.Stores");
        body.GetProperty("result").GetProperty("rows").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("metricUsage").EnumerateArray().Select(e => e.GetString()).Should().Contain("TotalRevenue");
        body.GetProperty("chartSuggestion").GetProperty("type").GetString().Should().Be("bar");
        body.GetProperty("explanation").GetString().Should().NotBeNullOrWhiteSpace();
        ConversationIdOf(body).Should().MatchRegex("^[0-9a-f]{32}$");
        factory.Store.CreatedCount.Should().Be(1);
    }

    [Fact]
    public async Task ZeroRowResult_ReturnsEmptyRowsAndTableOnlyChart()
    {
        await using var factory = new AskApiFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();

        var (status, body) = await AskAsync(client, ApiScripts.NoRows);

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("result").GetProperty("rows").GetArrayLength().Should().Be(0);
        body.GetProperty("chartSuggestion").GetProperty("type").GetString().Should().Be("table-only");
    }

    [Fact]
    public async Task FollowUp_WithReturnedConversationId_ReusesConversation()
    {
        await using var factory = new AskApiFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();

        var (_, first) = await AskAsync(client, ApiScripts.RevenueByRegion);
        var id = ConversationIdOf(first);
        var (status, second) = await AskAsync(client, ApiScripts.WestOnly, id);

        status.Should().Be(HttpStatusCode.OK);
        ConversationIdOf(second).Should().Be(id);
        second.GetProperty("sql").GetString().Should().Contain("'West'");
        second.GetProperty("result").GetProperty("rows").GetArrayLength().Should().Be(1);
        factory.Store.CreatedCount.Should().Be(1);
    }

    [Fact]
    public async Task UnknownConversationId_Returns404NamingItAndCreatesNoConversation()
    {
        await using var factory = new AskApiFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();
        var unknown = new string('a', 32);

        var (status, body) = await AskAsync(client, ApiScripts.WestOnly, unknown);

        status.Should().Be(HttpStatusCode.NotFound);
        body.GetProperty("detail").GetString().Should().Contain(unknown);
        factory.Store.CreatedCount.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredConversationId_Returns404NamingItAndCreatesNoNewConversation()
    {
        await using var factory = new AskApiFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();
        var (_, first) = await AskAsync(client, ApiScripts.RevenueByRegion);
        var id = ConversationIdOf(first);

        factory.Time.Advance(TimeSpan.FromMinutes(61));
        var (status, body) = await AskAsync(client, ApiScripts.WestOnly, id);

        status.Should().Be(HttpStatusCode.NotFound);
        body.GetProperty("detail").GetString().Should().Contain(id);
        factory.Store.CreatedCount.Should().Be(1);
    }

    private static string ConversationIdOf(JsonElement body) => body.GetProperty("conversationId").GetString()!;
}

/// <summary>Host startup checks that need no database (kept apart so they run without Docker).</summary>
public sealed class AskApiStartupTests
{
    [Fact]
    public void UnknownProvider_FailsHostStartupNamingIt()
    {
        using var factory = new AskApiFactory(connectionString: null, provider: "NoSuchProvider");

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Where(e => e.ToString().Contains("NoSuchProvider"));
    }
}
