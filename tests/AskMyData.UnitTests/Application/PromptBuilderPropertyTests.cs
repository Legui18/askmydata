using System.Text.Json;
using AskMyData.Application.Llm;
using AskMyData.Application.Prompting;
using AskMyData.Domain.Conversations;
using AskMyData.Domain.Semantic;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Application;

/// <summary>Property tests for <see cref="PromptBuilder"/> (R1.3, R2.1, R5.6, R8.1, R8.2, R10.1).</summary>
public class PromptBuilderPropertyTests
{
    private static readonly SemanticLayer Layer = SemanticLayerCatalog.Create();

    private const string FreshNonce = "qq77fresh";

    private static readonly string[] Fragments =
    [
        "a", "b", "revenue", " ", "\n", "\"", "\\", "'", "SELECT 1", "--", "{}", "\u00e9", "\u4e2d",
        "<<<UNTRUSTED_", "<<<END_UNTRUSTED_", ">>>", "<<<UNTRUSTED_qq77>>>", "<<<END_UNTRUSTED_qq77>>>",
    ];

    // Each role gets a distinct section-sign prefix so no untrusted text can coincide with trusted prompt text.
    private static Gen<string> TextGen(string prefix) =>
        Gen.Elements(Fragments).ArrayOf().Select(a => prefix + string.Concat(a));

    private sealed class ScriptedNonces(params string[] values) : INonceSource
    {
        private int _i;
        public string Next() => values[Math.Min(_i++, values.Length - 1)];
    }

    private sealed record Scenario(
        string Question,
        List<Turn> History,
        string FailingSql,
        bool Repair,
        bool CollideFirstNonce);

    private static Gen<Scenario> ScenarioGen =>
        from question in TextGen("\u00a7Q:")
        from n in Gen.Choose(0, 15)
        from qs in TextGen("\u00a7H:").ListOf(n)
        from sqls in TextGen("\u00a7S:").ListOf(n)
        from failing in TextGen("\u00a7F:")
        from repair in Gen.Elements(true, false)
        from collide in Gen.Elements(true, false)
        select new Scenario(
            question,
            qs.Zip(sqls, (q, sql) => (q, sql))
                .Select((t, i) => new Turn(i + 1, t.q, t.sql, "explanation", DateTimeOffset.UnixEpoch))
                .ToList(),
            failing,
            repair,
            collide);

    // Feature: askmydata-conversational-analytics, Property 16: Prompt composition and untrusted delimiting
    // **Validates: Requirements 1.3, 2.1, 5.6, 8.1, 8.2, 10.1**
    [Property(MaxTest = 100)]
    public Property Prompt_is_composed_from_trusted_system_text_and_a_nonce_delimited_untrusted_section() =>
        Prop.ForAll(
            Arb.From(ScenarioGen),
            s =>
            {
                var nonces = s.CollideFirstNonce
                    ? new ScriptedNonces(s.Question, FreshNonce)
                    : new ScriptedNonces(FreshNonce);
                var sut = new PromptBuilder(Layer, nonces);
                RepairInfo? repair = s.Repair ? new RepairInfo("EXECUTION_ERROR", null) : null;

                var req = sut.Build(s.Question, s.History, s.FailingSql, repair);

                // System_Prompt: Metric definitions and Allowlist schema (R1.3).
                foreach (var m in Layer.Metrics)
                {
                    req.SystemPrompt.Should().Contain(m.Name).And.Contain(m.SqlExpression);
                }

                foreach (var e in Layer.Allowlist)
                {
                    req.SystemPrompt.Should().Contain(e.Name);
                    foreach (var c in e.Columns)
                    {
                        req.SystemPrompt.Should().Contain(c.Name);
                    }
                }

                // Untrusted text never appears in the System_Prompt (R2.1, R10.1).
                var recent = s.History.TakeLast(10).ToList();
                var untrusted = new List<string> { s.Question };
                untrusted.AddRange(s.History.SelectMany(t => new[] { t.Question, t.Sql }));
                untrusted.Add(s.FailingSql);
                foreach (var text in untrusted)
                {
                    req.SystemPrompt.Should().NotContain(text);
                }

                // Markers carry a nonce that occurs in none of the untrusted texts, even after a forced redraw.
                req.UntrustedSection.Should().StartWith("<<<UNTRUSTED_");
                var nonce = req.UntrustedSection["<<<UNTRUSTED_".Length..req.UntrustedSection.IndexOf(">>>", StringComparison.Ordinal)];
                nonce.Should().Be(FreshNonce);
                untrusted.Should().OnlyContain(t => !t.Contains(nonce, StringComparison.Ordinal));

                var open = $"<<<UNTRUSTED_{nonce}>>>\n";
                var close = $"\n<<<END_UNTRUSTED_{nonce}>>>";
                req.UntrustedSection.Should().StartWith(open).And.EndWith(close);

                // Payload decodes to question, last 10 turns chronologically (question and SQL only), failing SQL on repair (R8.1, R8.2, R5.6).
                var root = JsonDocument.Parse(req.UntrustedSection[open.Length..^close.Length]).RootElement;
                root.GetProperty("question").GetString().Should().Be(s.Question);

                var history = root.GetProperty("history").EnumerateArray().ToList();
                history.Should().HaveCount(recent.Count);
                for (var i = 0; i < recent.Count; i++)
                {
                    history[i].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("question", "sql");
                    history[i].GetProperty("question").GetString().Should().Be(recent[i].Question);
                    history[i].GetProperty("sql").GetString().Should().Be(recent[i].Sql);
                }

                if (s.Repair)
                {
                    req.CallType.Should().Be(LlmCallType.Repair);
                    root.GetProperty("failingSql").GetString().Should().Be(s.FailingSql);
                }
                else
                {
                    req.CallType.Should().Be(LlmCallType.Initial);
                    root.TryGetProperty("failingSql", out _).Should().BeFalse();
                }
            });
}
