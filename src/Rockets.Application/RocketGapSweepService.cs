using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rockets.Application;

public sealed class RocketGapSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RocketGapSweepService> _logger;
    private readonly RocketSequencerOptions _options;

    public RocketGapSweepService(
        IServiceScopeFactory scopeFactory, ILogger<RocketGapSweepService> logger, RocketSequencerOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SweepInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Rocket gap sweep failed; will retry on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during host shutdown; PeriodicTimer.WaitForNextTickAsync throws instead of returning false.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var sequencerScope = _scopeFactory.CreateScope();
        var sequencer = sequencerScope.ServiceProvider.GetRequiredService<IRocketEventSequencer>();
        var channels = await sequencer.GetStaleChannelsWithBacklogAsync(cancellationToken);

        foreach (var channel in channels)
        {
            try
            {
                using var channelScope = _scopeFactory.CreateScope();
                var ingestion = channelScope.ServiceProvider.GetRequiredService<RocketIngestionService>();

                var result = await ingestion.RecheckAsync(channel, cancellationToken);
                if (result.IsFailure)
                {
                    _logger.LogWarning("Gap sweep rejected channel {Channel}: {Error}", channel, result.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Gap sweep could not process channel {Channel}; will retry on the next tick.", channel);
            }
        }
    }
}
