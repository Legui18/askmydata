namespace AskMyData.Api.DemoGuard;

/// <summary>Demo_Guard configuration, bound from the <c>DemoGuard</c> section (R13.1, R13.2, R13.6).</summary>
public sealed class DemoGuardOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DemoGuard";

    /// <summary>Maximum admitted requests per client IP in any sliding 60 s window. 0 disables the limit.</summary>
    public int PerIpPerMinute { get; set; } = 20;

    /// <summary>Maximum admitted requests per UTC day across all clients (Daily_Cap). 0 disables the cap.</summary>
    public int DailyCap { get; set; } = 500;

    /// <summary>Proxy addresses trusted for forwarded headers. Forwarded headers are used only when non-empty.</summary>
    public string[] TrustedProxies { get; set; } = [];
}
