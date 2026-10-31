using System.Collections.Frozen;

namespace AskMyData.Domain.Semantic;

/// <summary>
/// The governed Metric definitions, the Allowlist and the Allowed_Table_Function_List
/// (Requirements 1.1, 1.2, 1.5, 3.11). Immutable once constructed.
/// </summary>
public sealed class SemanticLayer
{
    private readonly FrozenDictionary<string, Metric> _metricsByName;
    private readonly FrozenDictionary<string, AllowlistEntry> _objectsByQualifiedName;

    public SemanticLayer(
        IEnumerable<Metric> metrics,
        IEnumerable<AllowlistEntry> allowlist,
        IEnumerable<string>? allowedTableFunctions = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(allowlist);

        Metrics = metrics.ToArray();
        Allowlist = allowlist.ToArray();

        var metricMap = new Dictionary<string, Metric>(StringComparer.OrdinalIgnoreCase);
        foreach (var metric in Metrics)
        {
            ArgumentNullException.ThrowIfNull(metric, nameof(metrics));
            if (!metricMap.TryAdd(metric.Name, metric))
            {
                throw new ArgumentException($"Duplicate Metric name '{metric.Name}'.", nameof(metrics));
            }
        }

        var objectMap = new Dictionary<string, AllowlistEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Allowlist)
        {
            ArgumentNullException.ThrowIfNull(entry, nameof(allowlist));
            if (!objectMap.TryAdd(QualifiedName(entry.Schema, entry.Name), entry))
            {
                throw new ArgumentException(
                    $"Duplicate Allowlist object '{entry.Schema}.{entry.Name}'.", nameof(allowlist));
            }
        }

        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in allowedTableFunctions ?? [])
        {
            if (string.IsNullOrWhiteSpace(function))
            {
                throw new ArgumentException(
                    "Allowed table function names must be non-empty.", nameof(allowedTableFunctions));
            }

            functions.Add(function.Trim());
        }

        _metricsByName = metricMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _objectsByQualifiedName = objectMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        AllowedTableFunctions = functions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Metric> Metrics { get; }

    public IReadOnlyList<AllowlistEntry> Allowlist { get; }

    /// <summary>Table-valued functions the SQL may call. Empty by default (Requirement 3.11).</summary>
    public IReadOnlySet<string> AllowedTableFunctions { get; }

    /// <summary>Finds a Metric by name, ignoring case.</summary>
    public bool TryGetMetric(string name, out Metric metric)
    {
        if (name is not null && _metricsByName.TryGetValue(name, out var found))
        {
            metric = found;
            return true;
        }

        metric = null!;
        return false;
    }

    /// <summary>Finds an Allowlist table or view by schema and name, ignoring case.</summary>
    public bool TryGetObject(string schema, string name, out AllowlistEntry entry)
    {
        if (schema is not null && name is not null
            && _objectsByQualifiedName.TryGetValue(QualifiedName(schema, name), out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    // Unit separator avoids collisions such as ("a.b", "c") vs ("a", "b.c").
    private static string QualifiedName(string schema, string name) => schema + "\u001F" + name;
}
