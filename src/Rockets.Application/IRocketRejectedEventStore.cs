using Rockets.Domain;

namespace Rockets.Application;

public interface IRocketRejectedEventStore
{
    Task RecordAsync(RocketEvent @event, string reason, CancellationToken cancellationToken);
}
