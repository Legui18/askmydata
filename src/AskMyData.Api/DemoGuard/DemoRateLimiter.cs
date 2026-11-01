using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AskMyData.Api.DemoGuard;

/// <summary>Result of a Demo_Guard admission check.</summary>
/// <param name="Admitted">True when the request may proceed.</param>
/// <param name="Reason"><see cref="DemoRateLimiter.PerIpLimitReason"/> or <see cref="DemoRateLimiter.DailyCapReason"/> when rejected; otherwise null.</param>
/// <param name="RetryAfterSeconds">Seconds until a retry may succeed (at least 1 when rejected; 0 when admitted).</param>
public sealed record AdmissionDecision(bool Admitted, string? Reason, int RetryAfterSeconds);

/// <summary>
/// Clock-injected Demo_Guard limiter: a per-IP sliding 60 s window plus a global UTC-day Daily_Cap (KD11).
/// A limit of 0 disables that limit (R13.6). The IP timestamp is recorded only when a request is admitted.
/// </summary>
public sealed class DemoRateLimiter
{
    /// <summary>Rejection reason when the per-IP window is full.</summary>
    public const string PerIpLimitReason = "PER_IP_LIMIT";

    /// <summary>Rejection reason when the UTC-day cap is exhausted.</summary>
    public const string DailyCapReason = "DAILY_CAP";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private sealed class IpWindow
    {
        public readonly Queue<DateTimeOffset> Admissions = new();
    }

    private readonly TimeProvider _time;
    private readonly IOptions<DemoGuardOptions> _options;
    private readonly ConcurrentDictionary<string, IpWindow> _windows = new(StringComparer.Ordinal);
    private readonly object _dailyLock = new();
    private DateOnly _dailyDate;
    private int _dailyCount;

    /// <summary>Creates the limiter.</summary>
    public DemoRateLimiter(TimeProvider time, IOptions<DemoGuardOptions> options)
    {
        _time = time;
        _options = options;
    }

    /// <summary>Decides whether a request from <paramref name="clientIp"/> is admitted (R13.1, R13.2, R13.3).</summary>
    public AdmissionDecision TryAdmit(string clientIp)
    {
        ArgumentNullException.ThrowIfNull(clientIp);

        var opts = _options.Value;
        var now = _time.GetUtcNow();
        var window = _windows.GetOrAdd(clientIp, static _ => new IpWindow());

        lock (window)
        {
            if (opts.PerIpPerMinute > 0)
            {
                while (window.Admissions.Count > 0 && window.Admissions.Peek() + Window <= now)
                {
                    window.Admissions.Dequeue();
                }

                if (window.Admissions.Count >= opts.PerIpPerMinute)
                {
                    var wait = window.Admissions.Peek() + Window - now;
                    return new AdmissionDecision(false, PerIpLimitReason, CeilSeconds(wait));
                }
            }

            if (opts.DailyCap > 0)
            {
                lock (_dailyLock)
                {
                    var today = DateOnly.FromDateTime(now.UtcDateTime);
                    if (today != _dailyDate)
                    {
                        _dailyDate = today;
                        _dailyCount = 0;
                    }

                    if (_dailyCount >= opts.DailyCap)
                    {
                        var nextMidnight = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                        return new AdmissionDecision(false, DailyCapReason, CeilSeconds(nextMidnight - now));
                    }

                    _dailyCount++;
                }
            }

            if (opts.PerIpPerMinute > 0)
            {
                window.Admissions.Enqueue(now);
            }

            return new AdmissionDecision(true, null, 0);
        }
    }

    private static int CeilSeconds(TimeSpan span) =>
        (int)Math.Max(1, Math.Ceiling(span.TotalSeconds));
}
