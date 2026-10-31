namespace AskMyData.Application.Llm;

/// <summary>Raw output of one ILlmClient call plus its token usage.</summary>
/// <param name="RawText">Unparsed model output, expected to be a Structured_Response JSON object.</param>
/// <param name="PromptTokens">Prompt (input) tokens reported by the provider.</param>
/// <param name="CompletionTokens">Completion (output) tokens reported by the provider.</param>
/// <param name="ModelName">Name of the model that produced the output.</param>
public sealed record LlmResponse(string RawText, int PromptTokens, int CompletionTokens, string ModelName);
