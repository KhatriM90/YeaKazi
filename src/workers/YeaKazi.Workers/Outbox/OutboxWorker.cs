using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YeaKazi.Workers.Outbox;

public sealed class OutboxWorker(OutboxDispatcher dispatcher, IOptions<OutboxOptions> options, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(options.Value.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await dispatcher.DispatchBatchAsync(stoppingToken);
                if (handled > 0)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
