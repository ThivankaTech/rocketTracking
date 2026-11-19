using Rockets.Domain;

namespace Rockets.Application;

public interface IRocketEventSequencer
{
    Task<Result<IReadOnlyList<RocketEvent>>> SubmitAsync(RocketEvent @event, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RocketEvent>>> RecheckAsync(string channel, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetStaleChannelsWithBacklogAsync(CancellationToken cancellationToken);
}
