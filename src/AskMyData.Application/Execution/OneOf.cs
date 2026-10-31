namespace AskMyData.Application.Execution;

/// <summary>
/// Minimal discriminated union holding exactly one of two non-null values. A <c>default</c> instance holds
/// neither and throws on access.
/// </summary>
public readonly struct OneOf<T1, T2>
    where T1 : notnull
    where T2 : notnull
{
    private readonly T1? _first;
    private readonly T2? _second;
    private readonly byte _index; // 0 = uninitialized, 1 = T1, 2 = T2

    private OneOf(T1? first, T2? second, byte index)
    {
        _first = first;
        _second = second;
        _index = index;
    }

    /// <summary>True when the value is a <typeparamref name="T1"/>.</summary>
    public bool IsFirst => _index == 1;

    /// <summary>True when the value is a <typeparamref name="T2"/>.</summary>
    public bool IsSecond => _index == 2;

    /// <summary>Creates an instance holding <paramref name="value"/>.</summary>
    public static OneOf<T1, T2> FromFirst(T1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, default, 1);
    }

    /// <summary>Creates an instance holding <paramref name="value"/>.</summary>
    public static OneOf<T1, T2> FromSecond(T2 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(default, value, 2);
    }

    public static implicit operator OneOf<T1, T2>(T1 value) => FromFirst(value);

    public static implicit operator OneOf<T1, T2>(T2 value) => FromSecond(value);

    /// <summary>Returns the <typeparamref name="T1"/> value when present.</summary>
    public bool TryGetFirst(out T1 value)
    {
        value = _first!;
        return _index == 1;
    }

    /// <summary>Returns the <typeparamref name="T2"/> value when present.</summary>
    public bool TryGetSecond(out T2 value)
    {
        value = _second!;
        return _index == 2;
    }

    /// <summary>Applies the function matching the held value.</summary>
    public TResult Match<TResult>(Func<T1, TResult> first, Func<T2, TResult> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _index switch
        {
            1 => first(_first!),
            2 => second(_second!),
            _ => throw new InvalidOperationException("OneOf instance is not initialized."),
        };
    }
}
