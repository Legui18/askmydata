using AskMyData.Application.Llm;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>
/// ILlmClient spy driven by a script. Records every <see cref="LlmRequest"/> (even when the call is cancelled or
/// fails) and waits each step's delay on the supplied <see cref="TimeProvider"/>, so tests advance a
/// <c>FakeTimeProvider</c> and cancellation is honored. When the script is exhausted the last step repeats.
/// </summary>
public sealed class ScriptedLlmClient(IReadOnlyList<LlmStep> script, TimeProvider time) : ILlmClient
{
    private readonly List<LlmRequest> _requests = [];
    private readonly List<CancellationToken> _tokens = [];
    private readonly List<DateTimeOffset> _starts = [];
    private readonly object _gate = new();

    /// <summary>Fake-clock time at which each call started, in call order.</summary>
    public IReadOnlyList<DateTimeOffset> StartTimes
    {
        get
        {
            lock (_gate)
            {
                return _starts.ToArray();
            }
        }
    }

    public string ProviderName => "Scripted";

    public IReadOnlyList<LlmRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Cancellation tokens passed to each call, in call order.</summary>
    public IReadOnlyList<CancellationToken> Tokens
    {
        get
        {
            lock (_gate)
            {
                return _tokens.ToArray();
            }
        }
    }

    public int CallCount => Requests.Count;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LlmStep step;
        lock (_gate)
        {
            if (script.Count == 0)
            {
                throw new InvalidOperationException("The LLM script is empty.");
            }

            step = script[Math.Min(_requests.Count, script.Count - 1)];
            _requests.Add(request);
            _tokens.Add(cancellationToken);
            _starts.Add(time.GetUtcNow());
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (step.Delay > TimeSpan.Zero)
        {
            await Task.Delay(step.Delay, time, cancellationToken).ConfigureAwait(false);
        }

        if (step.Kind == LlmStepKind.Unavailable)
        {
            throw new LlmUnavailableException(ProviderName, "Scripted provider failure.");
        }

        return new LlmResponse(step.RawText, step.PromptTokens, step.CompletionTokens, "scripted");
    }
}
