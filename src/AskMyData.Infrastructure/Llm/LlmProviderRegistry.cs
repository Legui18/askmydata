using AskMyData.Application.Llm;

namespace AskMyData.Infrastructure.Llm;

/// <summary>
/// Name-to-factory registry of <see cref="ILlmClient"/> providers; names are case-insensitive (R11.1, R11.2).
/// </summary>
public sealed class LlmProviderRegistry
{
    private readonly Dictionary<string, (string Name, Func<IServiceProvider, ILlmClient> Factory)> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers (or replaces) a provider factory under <paramref name="name"/>.</summary>
    public LlmProviderRegistry Register(string name, Func<IServiceProvider, ILlmClient> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[name] = (name, factory);
        return this;
    }

    /// <summary>
    /// Creates the client registered under <paramref name="providerName"/>. An unknown name throws
    /// <see cref="InvalidOperationException"/> naming the Provider_Name and the registered names.
    /// </summary>
    public ILlmClient Resolve(string providerName, IServiceProvider services)
    {
        if (providerName is not null && _factories.TryGetValue(providerName, out var entry))
        {
            return entry.Factory(services);
        }

        var registered = string.Join(", ", _factories.Values.Select(v => v.Name).Order(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException($"Unknown LLM provider '{providerName}'. Registered: {registered}.");
    }
}
