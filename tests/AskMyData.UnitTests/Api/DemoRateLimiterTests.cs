using AskMyData.Api.DemoGuard;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.UnitTests.Api;

public sealed class DemoRateLimiterTests
{
    private static (DemoRateLimiter Limiter, FakeTimeProvider Clock) Create(int perIp, int cap, DateTimeOffset? start = null)
    {
        var clock = new FakeTimeProvider(start ?? new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new DemoGuardOptions { PerIpPerMinute = perIp, DailyCap = cap });
        return (new DemoRateLimiter(clock, options), clock);
    }

    [Fact]
    public void PerIp_window_rejects_then_recovers_with_ceiling_retry_after()
    {
        var (limiter, clock) = Create(2, 0);
        limiter.TryAdmit("a").Admitted.Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        limiter.TryAdmit("a").Admitted.Should().BeTrue();

        var rejected = limiter.TryAdmit("a");
        rejected.Should().Be(new AdmissionDecision(false, "PER_IP_LIMIT", 60));
        limiter.TryAdmit("b").Admitted.Should().BeTrue();

        clock.Advance(TimeSpan.FromMilliseconds(59_500));
        limiter.TryAdmit("a").Admitted.Should().BeTrue();
    }

    [Fact]
    public void Daily_cap_rejects_and_does_not_record_ip_timestamp_then_resets_at_utc_midnight()
    {
        var (limiter, clock) = Create(2, 2, new DateTimeOffset(2025, 1, 1, 23, 59, 30, TimeSpan.Zero));
        limiter.TryAdmit("a").Admitted.Should().BeTrue();
        limiter.TryAdmit("b").Admitted.Should().BeTrue();

        limiter.TryAdmit("c").Should().Be(new AdmissionDecision(false, "DAILY_CAP", 30));
        limiter.TryAdmit("c").Reason.Should().Be("DAILY_CAP");

        clock.Advance(TimeSpan.FromSeconds(30));
        limiter.TryAdmit("c").Admitted.Should().BeTrue();
        limiter.TryAdmit("c").Admitted.Should().BeTrue();
        limiter.TryAdmit("c").Reason.Should().Be("PER_IP_LIMIT");
    }

    [Fact]
    public void Zero_disables_both_limits()
    {
        var (limiter, _) = Create(0, 0);
        for (var i = 0; i < 1000; i++)
        {
            limiter.TryAdmit("a").Admitted.Should().BeTrue();
        }
    }
}
