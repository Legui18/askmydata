namespace AskMyData.Application.Llm;

/// <summary>
/// Port through which the System accesses a large language model. Implementations live in
/// Infrastructure (R11.3, R11.4).
/// </summary>
public interface ILlmClient
{
    /// <summary>The Provider_Name this implementation is registered under (for example "Gemini" or "Stub").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Sends one request to the model. Throws <see cref="LlmUnavailableException"/> on transport or provider
    /// failure and honors <paramref name="cancellationToken"/>; the caller distinguishes LLM_Timeout,
    /// Request_Deadline expiry and client abort.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
