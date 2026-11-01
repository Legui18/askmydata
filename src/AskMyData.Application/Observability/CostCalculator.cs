using AskMyData.Application.Options;
using Microsoft.Extensions.Options;

namespace AskMyData.Application.Observability;

/// <summary>Estimates the USD cost of one LLM call from configured model prices (R7.3, R7.7).</summary>
public sealed class CostCalculator(IOptions<LlmPricingOptions> pricing)
{
    private const decimal TokensPerPriceUnit = 1_000_000m;

    /// <summary>Returns the estimated cost, or 0 when the model is not priced.</summary>
    public decimal Estimate(string modelName, int promptTokens, int completionTokens)
    {
        var models = pricing.Value.Models;
        if (string.IsNullOrEmpty(modelName) || models is null || !models.TryGetValue(modelName, out var price) || price is null)
        {
            return 0m;
        }

        return (promptTokens * price.InputPerMillionUsd + completionTokens * price.OutputPerMillionUsd) / TokensPerPriceUnit;
    }
}
