namespace AskMyData.Application.Llm;

/// <summary>Raised by an ILlmClient when the provider fails or cannot be reached.</summary>
public sealed class LlmUnavailableException(string providerName, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The Provider_Name of the failing ILlmClient.</summary>
    public string ProviderName { get; } = providerName;
}
