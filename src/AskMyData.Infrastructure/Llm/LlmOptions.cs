namespace AskMyData.Infrastructure.Llm;

/// <summary>LLM provider configuration (section <c>Llm</c>, R11.1).</summary>
public sealed class LlmOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Llm";

    /// <summary>Provider_Name of the active <c>ILlmClient</c>.</summary>
    public string Provider { get; set; } = GeminiLlmClient.Name;

    /// <summary>Gemini adapter settings.</summary>
    public GeminiOptions Gemini { get; set; } = new();
}

/// <summary>Settings for <see cref="GeminiLlmClient"/>. The API key comes from env/secret store and is never logged.</summary>
public sealed class GeminiOptions
{
    public string Model { get; set; } = "gemini-2.5-flash";

    public string ApiKey { get; set; } = "";

    public Uri BaseUrl { get; set; } = new("https://generativelanguage.googleapis.com/");
}
