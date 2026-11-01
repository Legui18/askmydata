using AskMyData.Api.Mapping;
using AskMyData.Application.Ask;
using Microsoft.AspNetCore.Mvc;

namespace AskMyData.Api.Controllers;

/// <summary>Thin HTTP adapter: DTO to command, outcome to mapped response. No business logic.</summary>
[ApiController]
[Route("api/ask")]
public sealed class AskController(
    AskQuestionHandler handler,
    OutcomeHttpMapper mapper,
    ResponseSanitizer sanitizer) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] AskRequestDto request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var command = new AskCommand(request.Question!, request.ConversationId, correlationId);

        var outcome = await handler.HandleAsync(command, cancellationToken);
        var mapped = sanitizer.Sanitize(mapper.Map(outcome, correlationId));

        if (!mapped.WritesResponse)
        {
            return new StatusCodeResult(mapped.StatusCode);
        }

        var result = new ObjectResult(mapped.Body) { StatusCode = mapped.StatusCode };
        if (mapped.Body is ProblemDetails)
        {
            result.ContentTypes.Add("application/problem+json");
        }

        return result;
    }
}
