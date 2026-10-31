using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

internal static class GenHelpers
{
    /// <summary>Runs the generators in order and collects their values.</summary>
    public static Gen<IReadOnlyList<T>> Sequence<T>(IEnumerable<Gen<T>> gens)
    {
        Gen<IReadOnlyList<T>> result = Gen.Constant<IReadOnlyList<T>>([]);
        foreach (var gen in gens)
        {
            var g = gen;
            result = from list in result
                     from value in g
                     select (IReadOnlyList<T>)[.. list, value];
        }

        return result;
    }

    /// <summary>Picks <c>min..max</c> distinct elements in random order.</summary>
    public static Gen<IReadOnlyList<T>> Distinct<T>(IReadOnlyList<T> items, int min, int max) =>
        from count in Gen.Choose(Math.Min(min, items.Count), Math.Min(max, items.Count))
        from shuffled in Gen.Shuffle(items.ToArray())
        select (IReadOnlyList<T>)shuffled.Take(count).ToArray();
}
