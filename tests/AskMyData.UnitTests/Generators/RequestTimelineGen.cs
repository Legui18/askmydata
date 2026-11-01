using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

/// <summary>One request in a Demo_Guard timeline: advance the clock, then request from an IP.</summary>
public sealed record TimelineStep(int AdvanceMs, string Ip);

/// <summary>A Demo_Guard configuration, a start instant and a sequence of timed requests.</summary>
public sealed record RequestTimeline(
    int PerIpPerMinute,
    int DailyCap,
    DateTimeOffset Start,
    IReadOnlyList<TimelineStep> Steps);

/// <summary>Generates request timelines that cross minute-window and UTC-midnight boundaries.</summary>
internal static class RequestTimelineGen
{
    private static readonly string[] Ips = ["10.0.0.1", "10.0.0.2", "10.0.0.3"];

    private static readonly Gen<DateTimeOffset> Start = Gen.OneOf(
        // Shortly before a UTC midnight (including a month/year rollover).
        from secondsBefore in Gen.Choose(1, 180)
        from ms in Gen.Choose(0, 999)
        from day in Gen.Elements(new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
        select day - TimeSpan.FromMilliseconds(secondsBefore * 1000L - ms),
        // Mid-day.
        from seconds in Gen.Choose(0, 86_000)
        select new DateTimeOffset(2025, 3, 10, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromSeconds(seconds));

    private static readonly Gen<int> Advance = Gen.Frequency(
        (4, Gen.Choose(0, 2_000)),
        (3, Gen.Elements(59_999, 60_000, 60_001, 30_000, 1_000, 999)),
        (2, Gen.Choose(0, 70_000)),
        (1, Gen.Choose(60_000, 200_000)));

    private static readonly Gen<TimelineStep> Step =
        from advance in Advance
        from ip in Gen.Elements(Ips)
        select new TimelineStep(advance, ip);

    /// <summary>Limits of 0 (disabled) are generated alongside small positive limits.</summary>
    public static Gen<RequestTimeline> Timeline =>
        from perIp in Gen.Frequency((1, Gen.Constant(0)), (4, Gen.Choose(1, 4)))
        from cap in Gen.Frequency((1, Gen.Constant(0)), (4, Gen.Choose(1, 6)))
        from start in Start
        from count in Gen.Choose(1, 40)
        from steps in Gen.ArrayOf(Step, count)
        select new RequestTimeline(perIp, cap, start, steps);
}
