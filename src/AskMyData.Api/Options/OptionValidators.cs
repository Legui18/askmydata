using AskMyData.Application.Options;
using AskMyData.Infrastructure.Conversations;
using AskMyData.Infrastructure.Execution;
using AskMyData.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace AskMyData.Api.Options;

/// <summary>Startup validation for <see cref="AskOptions"/>. Messages never contain configuration values.</summary>
public sealed class AskOptionsValidator : IValidateOptions<AskOptions>
{
    public ValidateOptionsResult Validate(string? name, AskOptions options)
    {
        var failures = new List<string>();
        if (options.RequestDeadline <= TimeSpan.Zero) failures.Add("Ask:RequestDeadline must be positive.");
        if (options.LlmTimeout <= TimeSpan.Zero) failures.Add("Ask:LlmTimeout must be positive.");
        if (options.CommandTimeout <= TimeSpan.Zero) failures.Add("Ask:CommandTimeout must be positive.");
        if (options.RowLimit < 1) failures.Add("Ask:RowLimit must be at least 1.");
        if (options.HistoryTurns < 0) failures.Add("Ask:HistoryTurns must not be negative.");
        if (options.MaxExplanationLength < 1) failures.Add("Ask:MaxExplanationLength must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Startup validation for <see cref="QueryExecutorOptions"/>; the connection string is never echoed.</summary>
public sealed class QueryExecutorOptionsValidator : IValidateOptions<QueryExecutorOptions>
{
    public ValidateOptionsResult Validate(string? name, QueryExecutorOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("ConnectionStrings:ReadOnly must be set to a non-empty value.");
        if (options.RowLimit < 1) failures.Add("Ask:RowLimit must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Startup validation for <see cref="LlmOptions"/>; the API key is never echoed.</summary>
public sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        if (string.Equals(options.Provider, GeminiLlmClient.Name, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.Gemini.ApiKey))
        {
            return ValidateOptionsResult.Fail("Llm:Gemini:ApiKey must be set to a non-empty value when Llm:Provider is Gemini.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>Startup validation for <see cref="ConversationStoreOptions"/>.</summary>
public sealed class ConversationStoreOptionsValidator : IValidateOptions<ConversationStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, ConversationStoreOptions options)
    {
        var failures = new List<string>();
        if (options.Ttl <= TimeSpan.Zero) failures.Add("Conversations:Ttl must be positive.");
        if (options.SweepInterval <= TimeSpan.Zero) failures.Add("Conversations:SweepInterval must be positive.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
