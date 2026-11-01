using System.Globalization;
using AskMyData.Api.DemoGuard;
using Microsoft.AspNetCore.Mvc;

namespace AskMyData.Api.Middleware;

/// <summary>
/// Demo_Guard (R13.3, R13.4, R13.5): rejects over-limit <c>/api/ask</c> requests with 429 problem details
/// before the Orchestrator runs. The client IP is the connection's remote address; forwarded headers are
/// honoured only when <c>UseForwardedHeaders</c> was configured with trusted proxies.
/// </summary>
public sealed class DemoGuardMiddleware
{
    private const string UnknownClient = "unknown";

    private readonly RequestDelegate _next;
    private readonly DemoRateLimiter _limiter;
    private readonly ILogger<DemoGuardMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public DemoGuardMiddleware(RequestDelegate next, DemoRateLimiter limiter, ILogger<DemoGuardMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
    }

    /// <summary>Admits the request or answers 429 with <c>Retry-After</c> and a <c>reason</c> extension.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        var decision = _limiter.TryAdmit(clientIp);
        if (decision.Admitted)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Demo_Guard rejected request. ClientIp={ClientIp} Reason={Reason} CorrelationId={CorrelationId}",
            clientIp,
            decision.Reason,
            context.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
        };
        problem.Extensions["reason"] = decision.Reason;
        problem.Extensions["correlationId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}
