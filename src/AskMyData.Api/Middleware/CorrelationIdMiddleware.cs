namespace AskMyData.Api.Middleware;

/// <summary>
/// Assigns a fresh Correlation_Id to every request (R7.1). Inbound correlation headers are ignored so
/// callers cannot forge or collide ids. The id is exposed as <c>TraceIdentifier</c>, the
/// <c>X-Correlation-Id</c> response header and a <c>{CorrelationId}</c> logging scope.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Name of the response header carrying the Correlation_Id.</summary>
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Runs the rest of the pipeline inside the correlation scope.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
