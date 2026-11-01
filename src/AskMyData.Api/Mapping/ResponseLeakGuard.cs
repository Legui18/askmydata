namespace AskMyData.Api.Mapping;

/// <summary>
/// Removes text that matches the system prompt instructions from outgoing response strings.
/// Matching uses 8-word shingles of the normalized (lower-cased, whitespace-collapsed) instruction text.
/// </summary>
public sealed class ResponseLeakGuard
{
    public const int ShingleSize = 8;
    public const string Redaction = "[redacted]";
    public const string LeakWarning = "Response text matching internal instructions was removed.";

    private readonly HashSet<string> _shingles;

    public ResponseLeakGuard(string systemPromptInstructionText)
    {
        ArgumentNullException.ThrowIfNull(systemPromptInstructionText);
        var words = Tokenize(systemPromptInstructionText).Select(t => t.Word).ToArray();
        _shingles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + ShingleSize <= words.Length; i++)
        {
            _shingles.Add(string.Join(' ', words, i, ShingleSize));
        }
    }

    /// <summary>
    /// Replaces every span matching an instruction shingle with <c>[redacted]</c>.
    /// <paramref name="redacted"/> is set to true when at least one span was replaced.
    /// </summary>
    public string Redact(string? text, out bool redacted)
    {
        redacted = false;
        if (string.IsNullOrEmpty(text) || _shingles.Count == 0)
        {
            return text ?? string.Empty;
        }

        var tokens = Tokenize(text);
        if (tokens.Count < ShingleSize)
        {
            return text;
        }

        // Mark tokens covered by any matching shingle.
        var covered = new bool[tokens.Count];
        var any = false;
        for (var i = 0; i + ShingleSize <= tokens.Count; i++)
        {
            var key = string.Join(' ', tokens.Skip(i).Take(ShingleSize).Select(t => t.Word));
            if (_shingles.Contains(key))
            {
                any = true;
                for (var j = i; j < i + ShingleSize; j++)
                {
                    covered[j] = true;
                }
            }
        }

        if (!any)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder();
        var pos = 0;
        var k = 0;
        while (k < tokens.Count)
        {
            if (!covered[k])
            {
                k++;
                continue;
            }

            var runStart = k;
            while (k < tokens.Count && covered[k])
            {
                k++;
            }

            sb.Append(text, pos, tokens[runStart].Start - pos);
            sb.Append(Redaction);
            pos = tokens[k - 1].End;
        }

        sb.Append(text, pos, text.Length - pos);
        redacted = true;
        return sb.ToString();
    }

    /// <summary>Redacts every string in order; the leak warning is returned once when any string changed.</summary>
    public IReadOnlyList<string> RedactAll(IEnumerable<string> texts, out bool redacted)
    {
        ArgumentNullException.ThrowIfNull(texts);
        redacted = false;
        var result = new List<string>();
        foreach (var t in texts)
        {
            result.Add(Redact(t, out var r));
            redacted |= r;
        }

        return result;
    }

    private static List<(string Word, int Start, int End)> Tokenize(string text)
    {
        var tokens = new List<(string, int, int)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i > start)
            {
                tokens.Add((text.Substring(start, i - start).ToLowerInvariant(), start, i));
            }
        }

        return tokens;
    }
}
