using System.Text.Json;
using System.Text.Json.Nodes;
using AskMyData.Application.Llm;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Application;

/// <summary>Property tests for <see cref="StructuredResponseParser"/> (R2.2, R2.11).</summary>
public class StructuredResponseParserPropertyTests
{
    private static readonly StructuredResponseParser Parser = new();

    private static readonly string[] RequiredNames = ["sql", "metricUsage", "explanation", "adHocCalculation"];

    // No backticks: a fence marker inside a value would legitimately make a fenced response invalid.
    private static readonly char[] Alphabet =
        "abcXYZ019 _-.,;:*()[]{}<>=+/\\\"'\n\t\u00e9\u4e2d\u20ac".ToCharArray();

    private static Gen<string> TextGen(int minLength) =>
        Gen.Elements(Alphabet).ArrayOf().Select(a => new string(a))
            .Where(s => s.Length >= minLength);

    private static Gen<string> NonBlankTextGen =>
        TextGen(1).Where(s => !string.IsNullOrWhiteSpace(s));

    private static Gen<StructuredResponse> ValidResponseGen =>
        from kind in Gen.Choose(0, 2) // 0: sql only, 1: refusal only (empty sql), 2: both
        from sql in NonBlankTextGen
        from refusal in NonBlankTextGen
        from metrics in NonBlankTextGen.ListOf().Select(l => (IReadOnlyList<string>)l.ToList())
        from explanation in TextGen(0)
        from adHoc in Gen.Elements(true, false)
        select new StructuredResponse(
            kind == 1 ? "" : sql,
            metrics,
            explanation,
            adHoc,
            kind == 0 ? null : refusal);

    private static JsonObject ToJson(StructuredResponse r, bool writeNullRefusal = false)
    {
        var obj = new JsonObject
        {
            ["sql"] = r.Sql,
            ["metricUsage"] = new JsonArray(r.MetricUsage.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
            ["explanation"] = r.Explanation,
            ["adHocCalculation"] = r.AdHocCalculation,
        };
        if (r.Refusal is not null)
        {
            obj["refusal"] = r.Refusal;
        }
        else if (writeNullRefusal)
        {
            obj["refusal"] = null;
        }

        return obj;
    }

    private static string Serialize(JsonNode node) => node.ToJsonString();

    private static void AssertEqual(StructuredResponse? actual, StructuredResponse expected)
    {
        actual.Should().NotBeNull();
        actual!.Sql.Should().Be(expected.Sql);
        actual.MetricUsage.Should().Equal(expected.MetricUsage);
        actual.Explanation.Should().Be(expected.Explanation);
        actual.AdHocCalculation.Should().Be(expected.AdHocCalculation);
        actual.Refusal.Should().Be(expected.Refusal);
    }

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Round_trip_returns_an_equal_value_with_or_without_a_single_fence() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from nullRefusal in Gen.Elements(true, false)
                     from fence in Gen.Elements("", "```", "```json")
                     select (r, nullRefusal, fence)),
            t =>
            {
                var json = Serialize(ToJson(t.r, t.nullRefusal));
                var raw = t.fence.Length == 0 ? json : $"{t.fence}\n{json}\n```";

                AssertEqual(Parser.TryParse(raw), t.r);
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Missing_required_property_is_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from name in Gen.Elements(RequiredNames)
                     select (r, name)),
            t =>
            {
                var obj = ToJson(t.r);
                obj.Remove(t.name);

                Parser.TryParse(Serialize(obj)).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Unknown_property_is_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from name in Gen.Elements("extra", "Sql", "SQL", "metric_usage", "notes", "x1")
                     from value in Gen.Elements<JsonNode?>(JsonValue.Create(1), JsonValue.Create("v"), null)
                     select (r, name, value)),
            t =>
            {
                var obj = ToJson(t.r);
                obj[t.name] = t.value?.DeepClone();

                Parser.TryParse(Serialize(obj)).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Wrong_property_type_is_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from pick in Gen.Elements(
                         ("sql", "1"), ("sql", "null"), ("sql", "[]"),
                         ("explanation", "5"), ("explanation", "null"), ("explanation", "{}"),
                         ("adHocCalculation", "\"true\""), ("adHocCalculation", "1"), ("adHocCalculation", "null"),
                         ("metricUsage", "\"a\""), ("metricUsage", "null"), ("metricUsage", "[1]"),
                         ("metricUsage", "[null]"), ("metricUsage", "{}"),
                         ("refusal", "5"), ("refusal", "true"), ("refusal", "[]"))
                     select (r, pick)),
            t =>
            {
                var obj = ToJson(t.r);
                obj[t.pick.Item1] = JsonNode.Parse(t.pick.Item2);

                Parser.TryParse(Serialize(obj)).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Non_object_json_is_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from shape in Gen.Elements(0, 1, 2, 3, 4, 5)
                     select (r, shape)),
            t =>
            {
                var raw = t.shape switch
                {
                    0 => new JsonArray(ToJson(t.r)).ToJsonString(),
                    1 => JsonSerializer.Serialize(t.r.Sql),
                    2 => "42",
                    3 => "true",
                    4 => "null",
                    _ => "[]",
                };

                Parser.TryParse(raw).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Empty_sql_without_refusal_is_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from refusalForm in Gen.Elements(0, 1, 2) // absent, null, empty string
                     select (r, refusalForm)),
            t =>
            {
                var obj = ToJson(t.r with { Sql = "", Refusal = null });
                if (t.refusalForm == 1) obj["refusal"] = null;
                if (t.refusalForm == 2) obj["refusal"] = "";

                Parser.TryParse(Serialize(obj)).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.2, 2.11**
    [Property(MaxTest = 100)]
    public Property Garbage_text_or_multiple_fences_are_invalid() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from garbage in Gen.Elements("Here you go:", "Sure!", "oops", "}", "```")
                     from form in Gen.Choose(0, 4)
                     select (r, garbage, form)),
            t =>
            {
                var json = Serialize(ToJson(t.r));
                var raw = t.form switch
                {
                    0 => $"{t.garbage}\n{json}",
                    1 => $"{json}\n{t.garbage}",
                    2 => $"```json\n```json\n{json}\n```\n```",
                    3 => $"```json\n{json}\n```\n```json\n{json}\n```",
                    _ => $"```json\n{json}", // unterminated fence
                };

                // "```" garbage prefixed to unfenced JSON would be an unterminated fence, also invalid.
                Parser.TryParse(raw).Should().BeNull();
            });

    // Feature: askmydata-conversational-analytics, Property 10: Structured_Response parsing round trip and strictness
    // **Validates: Requirements 2.11, 2.13**
    [Property(MaxTest = 100)]
    public Property Non_empty_refusal_is_preserved_whether_or_not_sql_is_present() =>
        Prop.ForAll(
            Arb.From(from r in ValidResponseGen
                     from sql in Gen.Elements("", "SELECT 1", "DROP TABLE x")
                     select (r with { Sql = sql, Refusal = string.IsNullOrEmpty(r.Refusal) ? "No." : r.Refusal })),
            r =>
            {
                var parsed = Parser.TryParse(Serialize(ToJson(r)));

                parsed.Should().NotBeNull();
                parsed!.Refusal.Should().Be(r.Refusal);
                parsed.Sql.Should().Be(r.Sql);
            });
}
