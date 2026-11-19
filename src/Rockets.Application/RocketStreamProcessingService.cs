using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rockets.Domain;

namespace Rockets.Application;

public sealed class RocketStreamProcessingService : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RocketStreamProcessingService> _logger;

    public RocketStreamProcessingService(IServiceScopeFactory scopeFactory, ILogger<RocketStreamProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var consumerScope = _scopeFactory.CreateScope();
        var consumer = consumerScope.ServiceProvider.GetRequiredService<IRocketMessageConsumer>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var delivery in consumer.ConsumeAsync(stoppingToken))
                {
                    await ProcessAsync(consumer, delivery, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rocket stream consumption loop failed; restarting after a short delay.");
                await Task.Delay(RestartDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(
        IRocketMessageConsumer consumer, RocketMessageDelivery delivery, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<RocketIngestionService>();

        Result<Rocket?> result;
        try
        {
            result = await ingestion.HandleAsync(delivery.Event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Transient failure processing {Channel}/{MessageNumber}; leaving unacknowledged for redelivery.",
                delivery.Event.ChannelId, delivery.Event.MessageNumber);
            return;
        }

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Rejected message {Channel}/{MessageNumber}: {Error}",
                delivery.Event.ChannelId, delivery.Event.MessageNumber, result.Error);
        }

        await consumer.AcknowledgeAsync(delivery.DeliveryToken, cancellationToken);
    }
}
