using AskMyData.Application.Llm;
using AskMyData.Application.Prompting;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Llm;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;
using Xunit;

namespace AskMyData.UnitTests.Infrastructure.Llm;

public sealed class StubLlmTests
{
    private const string Demo = "What was total revenue by region in 2024?";
    private const string FollowUp = "Only for the West region";

    private static readonly SemanticLayer Layer = SemanticLayerCatalog.Create();

    private static LlmRequest Request(string question, LlmCallType type = LlmCallType.Initial) =>
        new PromptBuilder(Layer, new FixedNonce()).Build(
            question,
            [],
            type == LlmCallType.Repair ? "SELECT 1" : null,
            type == LlmCallType.Repair ? new RepairInfo("PARSE_ERROR", null) : null);

    private sealed class FixedNonce : INonceSource
    {
        public string Next() => "n0nce";
    }

    [Fact]
    public async Task Returns_scripted_output_by_call_index_with_fixed_tokens()
    {
        var source = new InMemoryStubScriptSource().Add(Demo, "first", "second");
        var client = new StubLlmClient(source);

        var initial = await client.CompleteAsync(Request("  WHAT was total   revenue by region in 2024? "), default);
        var repair = await client.CompleteAsync(Request(Demo, LlmCallType.Repair), default);

        initial.RawText.Should().Be("first");
        repair.RawText.Should().Be("second");
        initial.Should().BeEquivalentTo(new { PromptTokens = 100, CompletionTokens = 50, ModelName = "stub" });
        client.ProviderName.Should().Be("Stub");
    }

    [Fact]
    public async Task Unscripted_question_or_missing_index_returns_a_refusal()
    {
        var client = new StubLlmClient(new InMemoryStubScriptSource().Add(Demo, "only-initial"));

        var unknown = await client.CompleteAsync(Request("something else"), default);
        var noRepair = await client.CompleteAsync(Request(Demo, LlmCallType.Repair), default);

        foreach (var response in new[] { unknown, noRepair })
        {
            response.RawText.Should().Be(StubLlmClient.RefusalRawText);
            new StructuredResponseParser().TryParse(response.RawText)!.Refusal.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Shipped_script_file_has_valid_responses_that_pass_the_validator()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "stub", "stub-scripts.json");
        var source = new FileStubScriptSource(path);
        var validator = new ScriptDomSqlValidator(Layer);

        foreach (var question in new[] { Demo, FollowUp })
        {
            source.TryGetScript(question, out var responses).Should().BeTrue();
            var parsed = new StructuredResponseParser().TryParse(responses[0]);
            parsed.Should().NotBeNull();
            parsed!.MetricUsage.Should().Equal("TotalRevenue");
            validator.Validate(parsed.Sql, parsed.MetricUsage).Passed.Should().BeTrue();
        }
    }

    [Fact]
    public void Registry_resolves_case_insensitively()
    {
        var stub = new StubLlmClient(new InMemoryStubScriptSource());
        var registry = new LlmProviderRegistry().Register("Stub", _ => stub);

        registry.Resolve("sTuB", null!).Should().BeSameAs(stub);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("")]
    public void Registry_unknown_name_throws_naming_provider_and_registered_names(string name)
    {
        var registry = new LlmProviderRegistry()
            .Register("Stub", _ => null!)
            .Register("Gemini", _ => null!);

        var act = () => registry.Resolve(name, null!);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be($"Unknown LLM provider '{name}'. Registered: Gemini, Stub.");
    }
}
