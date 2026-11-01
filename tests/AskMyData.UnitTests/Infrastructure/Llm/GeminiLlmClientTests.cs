using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AskMyData.Application.Llm;
using AskMyData.Infrastructure.Llm;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskMyData.UnitTests.Infrastructure.Llm;

public sealed class GeminiLlmClientTests
{
    private const string ApiKey = "test-key-123";

    private static readonly LlmRequest Request =
        new("SYSTEM RULES", "UNTRUSTED QUESTION", LlmCallType.Initial, null);

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static GeminiLlmClient Create(FakeHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new LlmOptions { Gemini = new GeminiOptions { Model = "gemini-test", ApiKey = ApiKey } }));

    private const string OkBody = """
        {
          "candidates": [ { "content": { "parts": [ { "text": "{\"sql\":\"SELECT 1\"}" } ] } } ],
          "usageMetadata": { "promptTokenCount": 11, "candidatesTokenCount": 7 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_SendsSystemInstructionSchemaAndApiKeyHeader()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkBody);

        await Create(handler).CompleteAsync(Request, CancellationToken.None);

        handler.Captured!.Method.Should().Be(HttpMethod.Post);
        handler.Captured.RequestUri!.AbsoluteUri.Should().EndWith("models/gemini-test:generateContent");
        handler.Captured.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be(ApiKey);
        handler.Captured.RequestUri.Query.Should().NotContain(ApiKey);

        var body = JsonNode.Parse(handler.CapturedBody!)!;
        ((string?)body["systemInstruction"]!["parts"]![0]!["text"]).Should().Be("SYSTEM RULES");
        ((string?)body["contents"]![0]!["parts"]![0]!["text"]).Should().Be("UNTRUSTED QUESTION");
        ((string?)body["generationConfig"]!["responseMimeType"]).Should().Be("application/json");
        body["generationConfig"]!["responseSchema"]!["required"]!.AsArray()
            .Select(n => (string?)n).Should().Contain("sql");
    }

    [Fact]
    public async Task CompleteAsync_ParsesTextUsageAndConfiguredModelName()
    {
        var response = await Create(new FakeHandler(HttpStatusCode.OK, OkBody))
            .CompleteAsync(Request, CancellationToken.None);

        response.RawText.Should().Be("{\"sql\":\"SELECT 1\"}");
        response.PromptTokens.Should().Be(11);
        response.CompletionTokens.Should().Be(7);
        response.ModelName.Should().Be("gemini-test");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task CompleteAsync_NonSuccessStatus_ThrowsLlmUnavailable(HttpStatusCode status)
    {
        var act = () => Create(new FakeHandler(status, "{}")).CompleteAsync(Request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<LlmUnavailableException>();
        ex.Which.Message.Should().NotContain(ApiKey);
    }

    [Theory]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[]}}]}""")]
    public async Task CompleteAsync_EmptyCandidates_ThrowsLlmUnavailable(string body)
    {
        var act = () => Create(new FakeHandler(HttpStatusCode.OK, body)).CompleteAsync(Request, CancellationToken.None);

        await act.Should().ThrowAsync<LlmUnavailableException>();
    }
}
