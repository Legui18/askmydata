using System.Collections.Immutable;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// A node of a Normalized_Syntax_Tree. <see cref="Kind"/> names the node type (a ScriptDom
/// fragment type, a property slot, or a normalized leaf such as <c>column</c>),
/// <see cref="Value"/> holds a normalized leaf value, and <see cref="Children"/> are ordered.
/// </summary>
/// <remarks>
/// Comparison is purely structural: same kind, same value (ordinal) and pairwise structurally
/// equal children in the same order. A structural hash is computed once per node so most
/// mismatches are rejected without walking the subtree.
/// </remarks>
internal sealed record NormNode
{
    public NormNode(string kind, string? value, ImmutableArray<NormNode> children)
    {
        Kind = kind;
        Value = value;
        Children = children.IsDefault ? [] : children;
        StructuralHash = ComputeHash(Kind, Value, Children);
    }

    public string Kind { get; }

    public string? Value { get; }

    public ImmutableArray<NormNode> Children { get; }

    /// <summary>Hash over kind, value and children; equal trees have equal hashes.</summary>
    public int StructuralHash { get; }

    /// <summary>True when both trees have the same shape, kinds, values and child order.</summary>
    public bool StructurallyEquals(NormNode? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (StructuralHash != other.StructuralHash
            || Children.Length != other.Children.Length
            || !string.Equals(Kind, other.Kind, StringComparison.Ordinal)
            || !string.Equals(Value, other.Value, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < Children.Length; i++)
        {
            if (!Children[i].StructurallyEquals(other.Children[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when this node or any descendant structurally equals <paramref name="candidate"/>.</summary>
    public bool ContainsSubtree(NormNode candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var pending = new Stack<NormNode>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.StructurallyEquals(candidate))
            {
                return true;
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return false;
    }

    private static int ComputeHash(string kind, string? value, ImmutableArray<NormNode> children)
    {
        var hash = new HashCode();
        hash.Add(StringComparer.Ordinal.GetHashCode(kind));
        hash.Add(value is null ? 0 : StringComparer.Ordinal.GetHashCode(value));
        foreach (var child in children)
        {
            hash.Add(child.StructuralHash);
        }

        return hash.ToHashCode();
    }
}
