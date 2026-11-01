namespace AskMyData.Application.Options;

/// <summary>Price of one model in USD per one million tokens.</summary>
public sealed class ModelPrice
{
    /// <summary>USD per one million prompt (input) tokens.</summary>
    public decimal InputPerMillionUsd { get; set; }

    /// <summary>USD per one million completion (output) tokens.</summary>
    public decimal OutputPerMillionUsd { get; set; }
}

/// <summary>Configured model prices used to estimate the cost of an LLM call (R7.3).</summary>
public sealed class LlmPricingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Llm:Pricing";

    /// <summary>Prices keyed by model name (case-insensitive).</summary>
    public Dictionary<string, ModelPrice> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
