using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AskMyData.Infrastructure.Conversations;

/// <summary>Background service that periodically removes expired Conversations (R12.3).</summary>
public sealed class ConversationSweeper(
    InMemoryConversationStore store,
    IOptions<ConversationStoreOptions> options,
    TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                store.SweepExpired();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
