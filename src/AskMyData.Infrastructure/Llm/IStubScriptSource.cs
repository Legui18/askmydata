using System.Text.Json;

namespace AskMyData.Infrastructure.Llm;

/// <summary>
/// Supplies recorded raw LLM outputs to <see cref="StubLlmClient"/>, keyed by question text (R6.6, R11.4).
/// Index 0 of a script is the initial call output, index 1 the Repair_Call output.
/// </summary>
public interface IStubScriptSource
{
    /// <summary>Looks up the scripted raw outputs for a question; matching ignores case and extra whitespace.</summary>
    bool TryGetScript(string question, out IReadOnlyList<string> responses);
}

/// <summary>Question normalization shared by script sources and the stub client.</summary>
internal static class StubQuestionKey
{
    public static string Normalize(string question) =>
        string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}

/// <summary>In-memory script source for unit tests and the Eval_Suite.</summary>
public sealed class InMemoryStubScriptSource : IStubScriptSource
{
    private readonly Dictionary<string, IReadOnlyList<string>> _scripts = new(StringComparer.Ordinal);

    public InMemoryStubScriptSource(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>? scripts = null)
    {
        if (scripts is null)
        {
            return;
        }

        foreach (var (question, responses) in scripts)
        {
            Add(question, responses.ToArray());
        }
    }

    /// <summary>Adds or replaces the script for a question.</summary>
    public InMemoryStubScriptSource Add(string question, params string[] responses)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(responses);
        _scripts[StubQuestionKey.Normalize(question)] = responses.ToArray();
        return this;
    }

    /// <inheritdoc />
    public bool TryGetScript(string question, out IReadOnlyList<string> responses)
    {
        if (_scripts.TryGetValue(StubQuestionKey.Normalize(question), out var found))
        {
            responses = found;
            return true;
        }

        responses = [];
        return false;
    }
}

/// <summary>
/// Reads scripts from a JSON file (<c>Llm:Stub:ScriptPath</c>): an object mapping each question to an array of
/// responses. Each response is either a JSON object (a Structured_Response) or a string of raw model output.
/// The file is read once, at construction, so a bad file fails startup.
/// </summary>
public sealed class FileStubScriptSource : IStubScriptSource
{
    private readonly InMemoryStubScriptSource _inner = new();

    public FileStubScriptSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Stub script file '{path}' was not found.", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Stub script file '{path}' must contain a JSON object.");
        }

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Stub script for question '{entry.Name}' in '{path}' must be an array of responses.");
            }

            var responses = entry.Value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())
                .ToArray();
            _inner.Add(entry.Name, responses);
        }
    }

    /// <inheritdoc />
    public bool TryGetScript(string question, out IReadOnlyList<string> responses) =>
        _inner.TryGetScript(question, out responses);
}
