using AskMyData.Application.Ask;
using AskMyData.Application.Charts;
using AskMyData.Application.Conversations;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using AskMyData.Application.Options;
using AskMyData.Application.Prompting;
using AskMyData.Domain.Semantic;
using AskMyData.Evals.Cases;
using AskMyData.Infrastructure.Conversations;
using AskMyData.Infrastructure.Llm;
using AskMyData.Infrastructure.Observability;
using AskMyData.Infrastructure.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AskMyData.Evals.Runner;

public enum EvalMode
{
    Stub,
    Live,
}

/// <summary>The per-case object graph: real Orchestrator, fresh store, counting executor.</summary>
public sealed record EvalHost(AskQuestionHandler Handler, IConversationStore Store, CountingQueryExecutor Executor);

public static class EvalHostFactory
{
    /// <summary>
    /// Composes the real Application and Infrastructure services for one Eval_Case in Stub_Mode (R6.6). The only
    /// ILlmClient is <see cref="StubLlmClient"/> fed with the case's in-memory script; no HttpClient is created.
    /// </summary>
    public static Func<EvalCase, EvalHost> CreateStub(IQueryExecutor innerExecutor, TimeProvider time) =>
        Create(innerExecutor, time, evalCase =>
        {
            var script = new InMemoryStubScriptSource().Add(evalCase.Question, evalCase.Stub.Responses.ToArray());
            return new StubLlmClient(script);
        });

    /// <summary>
    /// Composes the same graph for Live_Mode (R6.13): every case uses the supplied real <see cref="ILlmClient"/>
    /// (the Gemini adapter), reached only through the port.
    /// </summary>
    public static Func<EvalCase, EvalHost> CreateLive(IQueryExecutor innerExecutor, TimeProvider time, ILlmClient llm)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return Create(innerExecutor, time, _ => llm);
    }

    private static Func<EvalCase, EvalHost> Create(
        IQueryExecutor innerExecutor, TimeProvider time, Func<EvalCase, ILlmClient> llmFactory)
    {
        ArgumentNullException.ThrowIfNull(innerExecutor);
        var semanticLayer = SemanticLayerCatalog.Create();
        var validator = new ScriptDomSqlValidator(semanticLayer);
        var prompts = new PromptBuilder(semanticLayer, new RandomNonceSource());
        var parser = new StructuredResponseParser();
        var charts = new ChartSuggester();
        var audit = new JsonAuditLog(NullLogger<JsonAuditLog>.Instance);
        var askOptions = Options.Create(new AskOptions());

        return evalCase =>
        {
            var llm = llmFactory(evalCase);
            var store = new InMemoryConversationStore(Options.Create(new ConversationStoreOptions()), time);
            var executor = new CountingQueryExecutor(innerExecutor);
            var handler = new AskQuestionHandler(
                llm, validator, executor, store, audit, prompts, parser, charts, askOptions, time);
            return new EvalHost(handler, store, executor);
        };
    }
}
