using System.Text.Json;
using AskMyData.Api.DemoGuard;
using AskMyData.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AskMyData.UnitTests.Api;

public sealed class DemoGuardMiddlewareTests
{
    private static async Task<IHost> StartAsync(int perIp, int dailyCap)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddSingleton(TimeProvider.System);
                    s.AddSingleton<IOptions<DemoGuardOptions>>(
                        Options.Create(new DemoGuardOptions { PerIpPerMinute = perIp, DailyCap = dailyCap }));
                    s.AddSingleton<DemoRateLimiter>();
                })
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.UseWhen(
                        ctx => ctx.Request.Path.StartsWithSegments("/api/ask"),
                        b => b.UseMiddleware<DemoGuardMiddleware>());
                    app.Run(ctx => ctx.Response.WriteAsync("ok"));
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Rejects_over_limit_ask_with_429_reason_retry_after_and_correlation_id()
    {
        using var host = await StartAsync(perIp: 1, dailyCap: 0);
        using var client = host.GetTestClient();

        (await client.PostAsync("/api/ask", null)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var response = await client.PostAsync("/api/ask", null);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var correlation = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("reason").GetString().Should().Be(DemoRateLimiter.PerIpLimitReason);
        json.RootElement.GetProperty("correlationId").GetString().Should().Be(correlation);
    }

    [Fact]
    public async Task Does_not_guard_paths_other_than_api_ask()
    {
        using var host = await StartAsync(perIp: 1, dailyCap: 1);
        using var client = host.GetTestClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/health")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }
    }
}
