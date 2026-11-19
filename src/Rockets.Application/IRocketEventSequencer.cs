using Rockets.Domain;

namespace Rockets.Application;

public interface IRocketEventSequencer
{
    Task<Result<Rocket?>> SubmitAsync(
        RocketEvent @event,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken);

    Task<Result<Rocket?>> RecheckAsync(
        string channel,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetStaleChannelsWithBacklogAsync(CancellationToken cancellationToken);
}
