using AskMyData.Api.DemoGuard;
using AskMyData.UnitTests.Generators;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.UnitTests.Api;

/// <summary>Property 25: Demo_Guard limits and Retry-After.</summary>
public class DemoGuardProperties
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private static int CeilSeconds(TimeSpan span) => (int)Math.Max(1, Math.Ceiling(span.TotalSeconds));

    // Feature: askmydata-conversational-analytics, Property 25: Demo_Guard limits and Retry-After
    // **Validates: Requirements 13.1, 13.2, 13.3, 13.6**
    [Property(MaxTest = 100)]
    public Property Limiter_matches_the_sliding_window_and_utc_day_oracle() =>
        Prop.ForAll(
            RequestTimelineGen.Timeline.ToArbitrary(),
            timeline =>
            {
                var clock = new FakeTimeProvider(timeline.Start);
                var limiter = new DemoRateLimiter(
                    clock,
                    Options.Create(new DemoGuardOptions { PerIpPerMinute = timeline.PerIpPerMinute, DailyCap = timeline.DailyCap }));

                // Oracle state.
                var admittedByIp = new Dictionary<string, List<DateTimeOffset>>();
                DateOnly? day = null;
                var dailyCount = 0;

                foreach (var step in timeline.Steps)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(step.AdvanceMs));
                    var now = clock.GetUtcNow();

                    if (!admittedByIp.TryGetValue(step.Ip, out var stamps))
                    {
                        admittedByIp[step.Ip] = stamps = [];
                    }

                    AdmissionDecision expected;
                    var recent = stamps.Where(t => t + Window > now).ToList();
                    var today = DateOnly.FromDateTime(now.UtcDateTime);
                    if (day != today)
                    {
                        day = today;
                        dailyCount = 0;
                    }

                    if (timeline.PerIpPerMinute > 0 && recent.Count >= timeline.PerIpPerMinute)
                    {
                        expected = new AdmissionDecision(false, "PER_IP_LIMIT", CeilSeconds(recent.Min() + Window - now));
                    }
                    else if (timeline.DailyCap > 0 && dailyCount >= timeline.DailyCap)
                    {
                        var midnight = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                        expected = new AdmissionDecision(false, "DAILY_CAP", CeilSeconds(midnight - now));
                    }
                    else
                    {
                        expected = new AdmissionDecision(true, null, 0);
                        if (timeline.DailyCap > 0)
                        {
                            dailyCount++;
                        }

                        // Only admitted requests record an IP timestamp.
                        stamps.Add(now);
                    }

                    var actual = limiter.TryAdmit(step.Ip);
                    actual.Should().Be(expected, "request from {0} at {1:O}", step.Ip, now);
                    if (!actual.Admitted)
                    {
                        actual.RetryAfterSeconds.Should().BeGreaterThanOrEqualTo(1);
                    }
                }
            });
}
