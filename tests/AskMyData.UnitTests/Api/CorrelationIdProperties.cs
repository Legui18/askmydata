using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AskMyData.Api.Middleware;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskMyData.UnitTests.Api;

/// <summary>Property 22: Correlation_Id on every response and log entry.</summary>
public sealed partial class CorrelationIdProperties : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> SeenIds = new(StringComparer.Ordinal);

    private readonly CapturingLoggerProvider _logs = new();
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CorrelationIdProperties()
    {
        _host = new HostBuilder()
            .ConfigureLogging(l => l.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(_logs))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(async ctx =>
                    {
                        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Probe");
                        var count = int.Parse(ctx.Request.Query["logs"]!);
                        for (var i = 0; i < count; i++)
                        {
                            await Task.Yield();
                            logger.LogInformation("entry {Index} for {Trace}", i, ctx.TraceIdentifier);
                        }

                        ctx.Response.StatusCode = int.Parse(ctx.Request.Query["status"]!);
                        if (ctx.Response.StatusCode != StatusCodes.Status204NoContent)
                        {
                            await ctx.Response.WriteAsync("body");
                        }
                    });
                }))
            .Build();
        _host.Start();
        _client = _host.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Hex32();

    private static readonly Gen<int> Status =
        Gen.Elements(200, 201, 204, 400, 404, 422, 499, 500, 502, 503, 504);

    // Header-safe values only; includes well-formed 32-hex ids a caller might try to force.
    // An empty string means "no inbound header".
    private static readonly Gen<string> Inbound =
        Gen.OneOf(
            Gen.Constant(string.Empty),
            Gen.Elements("abc", "00000000000000000000000000000000", "not-a-guid", "x y", "1"),
            Gen.Elements("0123456789abcdef".ToCharArray()).ArrayOf(32).Select(c => new string(c)),
            Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray())
                .ArrayOf(40).Select(c => new string(c)));

    // Feature: askmydata-conversational-analytics, Property 22: Correlation_Id on every response and log entry
    // **Validates: Requirements 7.1**
    [Property(MaxTest = 100)]
    public Property Every_response_and_log_entry_carries_a_fresh_correlation_id() =>
        Prop.ForAll(
            Status.ToArbitrary(),
            Inbound.ToArbitrary(),
            Gen.Choose(1, 4).ToArbitrary(),
            (status, inbound, logCount) =>
            {
                _logs.Clear();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/?status={status}&logs={logCount}");
                if (inbound.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, inbound);
                }

                using var response = _client.SendAsync(request).GetAwaiter().GetResult();

                response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).Should().BeTrue();
                var id = values!.Single();

                ((int)response.StatusCode).Should().Be(status);
                Hex32().IsMatch(id).Should().BeTrue("the id is a lowercase N-format GUID");
                id.Should().NotBe(inbound, "inbound correlation headers are ignored");
                SeenIds.TryAdd(id, 0).Should().BeTrue("ids are unique across requests");

                var entries = _logs.Entries.Where(e => e.Category == "Probe").ToList();
                entries.Should().HaveCount(logCount);
                entries.Should().OnlyContain(e => e.Message.Contains(id));
                entries.Should().OnlyContain(e => e.ScopeCorrelationIds.Contains(id));
                return true;
            });

    private sealed record LogEntry(string Category, string Message, List<string> ScopeCorrelationIds);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public void Clear() => _entries.Clear();

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var ids = new List<string>();
                owner._scopes.ForEachScope(
                    static (scope, list) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                        {
                            foreach (var (key, value) in pairs)
                            {
                                if (key == "CorrelationId")
                                {
                                    list.Add(value.ToString()!);
                                }
                            }
                        }
                    },
                    ids);
                owner._entries.Enqueue(new LogEntry(category, formatter(state, exception), ids));
            }
        }
    }
}
