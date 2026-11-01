using AskMyData.Application.Llm;
using AskMyData.Infrastructure.Llm;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Llm;

public sealed class ProviderRegistryProperties
{
    private static readonly string[] Registered = ["Gemini", "Stub"];

    private static readonly Gen<char> NameChar =
        Gen.Elements("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_ .".ToCharArray());

    /// <summary>Provider_Names (possibly empty) that never case-insensitively equal a registered name.</summary>
    private static readonly Gen<string> UnknownName =
        Gen.Choose(0, 20)
            .SelectMany(len => Gen.ArrayOf(NameChar, len))
            .Select(chars => new string(chars))
            .Where(s => !Registered.Contains(s, StringComparer.OrdinalIgnoreCase));

    private static readonly Gen<string> RegisteredAnyCase =
        from name in Gen.Elements(Registered)
        from flips in Gen.ArrayOf(Gen.Elements(true, false), name.Length)
        select new string(name.Select((c, i) => flips[i] ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c)).ToArray());

    private sealed class NullClient : ILlmClient
    {
        public string ProviderName => "Null";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static LlmProviderRegistry NewRegistry(ILlmClient client)
    {
        var registry = new LlmProviderRegistry();
        foreach (var name in Registered)
        {
            registry.Register(name, _ => client);
        }

        return registry;
    }

    // Feature: askmydata-conversational-analytics, Property 27: Unknown provider names fail startup
    /// <summary>**Validates: Requirements 11.2**</summary>
    [Property(MaxTest = 100)]
    public Property Unknown_provider_name_throws_naming_provider_and_registered_names() =>
        Prop.ForAll(UnknownName.ToArbitrary(), name =>
        {
            var registry = NewRegistry(new NullClient());
            try
            {
                registry.Resolve(name, null!);
                return false.Label($"'{name}' resolved");
            }
            catch (InvalidOperationException ex)
            {
                return (ex.Message.Contains(name, StringComparison.Ordinal)
                        && Registered.All(r => ex.Message.Contains(r, StringComparison.Ordinal)))
                    .Label($"'{name}': {ex.Message}");
            }
        });

    // Feature: askmydata-conversational-analytics, Property 27: Unknown provider names fail startup
    /// <summary>**Validates: Requirements 11.2**</summary>
    [Property(MaxTest = 100)]
    public Property Registered_names_resolve_in_any_casing() =>
        Prop.ForAll(RegisteredAnyCase.ToArbitrary(), name =>
        {
            var client = new NullClient();
            var registry = NewRegistry(client);
            return ReferenceEquals(registry.Resolve(name, null!), client).Label(name);
        });
}
