using AskMyData.Api.Mapping;
using AskMyData.Api.Middleware;
using AskMyData.Api.Options;
using AskMyData.Application.Ask;
using AskMyData.Application.Charts;
using AskMyData.Application.Conversations;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using AskMyData.Application.Options;
using AskMyData.Application.Prompting;
using AskMyData.Application.Validation;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Conversations;
using AskMyData.Infrastructure.Execution;
using AskMyData.Infrastructure.Llm;
using AskMyData.Infrastructure.Observability;
using AskMyData.Infrastructure.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

var services = builder.Services;
var configuration = builder.Configuration;

services
    .AddControllers()
    .ConfigureApiBehaviorOptions(o =>
        o.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request is invalid.",
            };
            problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        });

// Options, validated at startup. Validation messages name keys only, never values.
services.AddOptions<AskOptions>().Bind(configuration.GetSection(AskOptions.SectionName)).ValidateOnStart();
services.AddOptions<ConversationStoreOptions>().Bind(configuration.GetSection("Conversations")).ValidateOnStart();
services.AddOptions<QueryExecutorOptions>()
    .Configure<IOptions<AskOptions>>((o, ask) =>
    {
        o.ConnectionString = configuration.GetConnectionString("ReadOnly") ?? string.Empty;
        o.RowLimit = ask.Value.RowLimit;
    })
    .ValidateOnStart();
services.AddSingleton<IValidateOptions<AskOptions>, AskOptionsValidator>();
services.AddSingleton<IValidateOptions<ConversationStoreOptions>, ConversationStoreOptionsValidator>();
services.AddSingleton<IValidateOptions<QueryExecutorOptions>, QueryExecutorOptionsValidator>();

services.AddSingleton(TimeProvider.System);

// Domain and Application services.
services.AddSingleton(_ => SemanticLayerCatalog.Create());
services.AddSingleton<ISqlValidator, ScriptDomSqlValidator>();
services.AddSingleton<StructuredResponseParser>();
services.AddSingleton<ChartSuggester>();
services.AddSingleton<INonceSource, RandomNonceSource>();
services.AddSingleton<PromptBuilder>();
services.AddSingleton<AskQuestionHandler>();

// Infrastructure adapters.
services.AddSingleton<IQueryExecutor, SqlServerQueryExecutor>();
services.AddSingleton<InMemoryConversationStore>();
services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemoryConversationStore>());
services.AddHostedService<ConversationSweeper>();
services.AddSingleton<IAuditLog, JsonAuditLog>();

// LLM providers: Stub is always registered; Llm:Provider is resolved eagerly below.
services.AddSingleton<IStubScriptSource>(_ =>
{
    var path = configuration["Llm:Stub:ScriptPath"] ?? "stub/stub-scripts.json";
    if (!Path.IsPathRooted(path))
    {
        path = Path.Combine(AppContext.BaseDirectory, path);
    }

    return new FileStubScriptSource(path);
});
services.AddSingleton(_ =>
    new LlmProviderRegistry().Register(StubLlmClient.Name, sp => new StubLlmClient(sp.GetRequiredService<IStubScriptSource>())));
services.AddSingleton<ILlmClient>(sp =>
    sp.GetRequiredService<LlmProviderRegistry>().Resolve(configuration["Llm:Provider"] ?? string.Empty, sp));

// Api adapters.
services.AddSingleton<OutcomeHttpMapper>();
services.AddSingleton(sp => new ResponseLeakGuard(sp.GetRequiredService<PromptBuilder>().SystemPromptInstructionText));
services.AddSingleton<ResponseSanitizer>();

var app = builder.Build();

// Fail startup now for an unknown provider (R11.2).
_ = app.Services.GetRequiredService<ILlmClient>();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
    {
        logger.LogError(exception, "Unhandled exception");
    }

    var problem = new AskProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "An unexpected error occurred.",
        CorrelationId = context.TraceIdentifier,
    };
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
}));
app.UseRouting();
app.MapControllers();

app.Run();

/// <summary>Entry point marker so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
