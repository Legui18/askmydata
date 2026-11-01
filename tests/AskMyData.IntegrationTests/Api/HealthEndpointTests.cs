using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace AskMyData.IntegrationTests.Api;

/// <summary>Health endpoint over the real Api pipeline (R7.11, R13.5).</summary>
public sealed class HealthEndpointTests(ApiDatasetFixture dataset) : IClassFixture<ApiDatasetFixture>
{
    private const string Unreachable =
        "Server=127.0.0.1,1;Database=AskMyData;User Id=x;Password=x;Encrypt=False;TrustServerCertificate=True;Connect Timeout=2";

    private sealed class HealthFactory(string connectionString, int perIp = 0, int dailyCap = 0)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Llm:Provider", "Stub");
            builder.UseSetting("DemoGuard:PerIpPerMinute", perIp.ToString());
            builder.UseSetting("DemoGuard:DailyCap", dailyCap.ToString());
            builder.UseSetting("ConnectionStrings:ReadOnly", connectionString);
        }
    }

    [Fact]
    public async Task Health_WithTestcontainersDatabase_Returns200Healthy()
    {
        await using var factory = new HealthFactory(dataset.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        body.RootElement.GetProperty("sqlServer").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_WithUnreachableConnectionString_Returns503WithoutLeakingDetails()
    {
        await using var factory = new HealthFactory(Unreachable);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
        body.RootElement.GetProperty("sqlServer").GetString().Should().Be("Unhealthy");
        text.Should().NotContain("127.0.0.1").And.NotContain("Password");
    }

    [Fact]
    public async Task Health_IsNeverRateLimited_AfterPerIpLimitAndDailyCapAreExhausted()
    {
        await using var factory = new HealthFactory(dataset.ConnectionString, perIp: 1, dailyCap: 1);
        using var client = factory.CreateClient();

        // The first request is admitted (400 from model validation); the rest are rejected by Demo_Guard.
        using var admitted = await client.PostAsJsonAsync("/api/ask", new { question = "   " });
        admitted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        for (var i = 0; i < 3; i++)
        {
            using var rejected = await client.PostAsJsonAsync("/api/ask", new { question = "   " });
            rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
