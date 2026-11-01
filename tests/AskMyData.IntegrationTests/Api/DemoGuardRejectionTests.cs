using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AskMyData.Api.DemoGuard;
using AskMyData.Api.Middleware;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AskMyData.IntegrationTests.Api;

/// <summary>
/// Demo_Guard rejection path over the real Api pipeline (R13.3, R13.4, R13.5). The LLM and the query
/// executor are spies, so no database is needed and any call to them fails the test.
/// </summary>
public sealed class DemoGuardRejectionTests
{
    private const string ClientIp = "203.0.113.9";

    private sealed class SpyLlmClient : ILlmClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string ProviderName => "Stub";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("LLM must not be called.");
        }
    }

    private sealed class SpyQueryExecutor : IQueryExecutor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(
            ValidatedSql sql, QueryBudget budget, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Executor must not be called.");
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            categoryName.EndsWith("DemoGuardMiddleware", StringComparison.Ordinal)
                ? new CapturingLogger(Messages)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>Sets a fixed remote IP (TestServer has none) so the logged client IP is meaningful.</summary>
    private sealed class FixedRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
                return nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class GuardedFactory : WebApplicationFactory<Program>
    {
        public SpyLlmClient Llm { get; } = new();

        public SpyQueryExecutor Executor { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Llm:Provider", "Stub");
            builder.UseSetting("DemoGuard:PerIpPerMinute", "1");
            builder.UseSetting("DemoGuard:DailyCap", "0");
            builder.UseSetting("ConnectionStrings:ReadOnly", "Server=localhost;Database=AskMyData");
            builder.ConfigureLogging(l => l.AddProvider(Logs));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter, FixedRemoteIpStartupFilter>();
                services.RemoveAll<ILlmClient>();
                services.AddSingleton<ILlmClient>(Llm);
                services.RemoveAll<IQueryExecutor>();
                services.AddSingleton<IQueryExecutor>(Executor);
            });
        }
    }

    [Fact]
    public async Task OverLimitRequest_Returns429WithRetryAfter_AndNeverReachesLlmOrExecutor()
    {
        await using var factory = new GuardedFactory();
        using var client = factory.CreateClient();

        using var first = await ConsumeAllowanceAsync(client);
        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var rejected = await client.PostAsJsonAsync("/api/ask", new { question = ApiScripts.RevenueByRegion });

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
        factory.Llm.Calls.Should().Be(0);
        factory.Executor.Calls.Should().Be(0);
    }

    /// <summary>
    /// Sends one admitted request that consumes the whole per-IP allowance (PerIpPerMinute = 1). A
    /// white-space question passes Demo_Guard, then fails model validation with 400 before the
    /// Orchestrator runs, so the spies stay at zero calls.
    /// </summary>
    private static Task<HttpResponseMessage> ConsumeAllowanceAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/ask", new { question = "   " });

    [Fact]
    public async Task Rejection_HasCorrelationIdHeaderMatchingBodyAndLogEntryWithIpAndReason()
    {
        await using var factory = new GuardedFactory();
        using var client = factory.CreateClient();
        using var first = await ConsumeAllowanceAsync(client);

        using var rejected = await client.PostAsJsonAsync("/api/ask", new { question = ApiScripts.RevenueByRegion });

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var correlationId = rejected.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        correlationId.Should().NotBeNullOrWhiteSpace();

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(correlationId);
        body.RootElement.GetProperty("reason").GetString().Should().Be(DemoRateLimiter.PerIpLimitReason);

        var entry = factory.Logs.Messages.Should().ContainSingle(m => m.Contains(correlationId, StringComparison.Ordinal)).Subject;
        entry.Should().Contain(ClientIp);
        entry.Should().Contain(DemoRateLimiter.PerIpLimitReason);
        factory.Llm.Calls.Should().Be(0);
        factory.Executor.Calls.Should().Be(0);
    }
}
