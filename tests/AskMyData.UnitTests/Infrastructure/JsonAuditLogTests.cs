using AskMyData.Application.Llm;
using AskMyData.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AskMyData.UnitTests.Infrastructure;

public sealed class JsonAuditLogTests
{
    private sealed class CapturingLogger : ILogger<JsonAuditLog>
    {
        public List<(LogLevel Level, Dictionary<string, object?> Fields)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
            {
                foreach (var kv in kvs)
                {
                    fields[kv.Key] = kv.Value;
                }
            }

            Entries.Add((logLevel, fields));
        }
    }

    [Fact]
    public void LlmCallFailed_logs_structured_fields_only()
    {
        var logger = new CapturingLogger();
        new JsonAuditLog(logger).LlmCallFailed("gemini", LlmCallType.Initial, TimeSpan.FromMilliseconds(1500), "corr-1", true);

        var (level, fields) = logger.Entries.Should().ContainSingle().Subject;
        level.Should().Be(LogLevel.Warning);
        fields["event"].Should().Be("llm.call.failed");
        fields["Provider"].Should().Be("gemini");
        fields["CallType"].Should().Be(nameof(LlmCallType.Initial));
        fields["ElapsedMs"].Should().Be(1500L);
        fields["CorrelationId"].Should().Be("corr-1");
        fields["TimedOut"].Should().Be(true);
        fields.Keys.Should().NotContain(k => k.Contains("Key") || k.Contains("Connection") || k.Contains("Row"));
    }

    [Fact]
    public void ValidationFailed_logs_reason_code_and_correlation_id()
    {
        var logger = new CapturingLogger();
        new JsonAuditLog(logger).ValidationFailed("NOT_SELECT", "corr-2");

        var (level, fields) = logger.Entries.Should().ContainSingle().Subject;
        level.Should().Be(LogLevel.Warning);
        fields["event"].Should().Be("validation.failed");
        fields["ReasonCode"].Should().Be("NOT_SELECT");
        fields["CorrelationId"].Should().Be("corr-2");
    }
}
