using Rockets.Domain;

namespace Rockets.Application;

public interface IRocketMessagePublisher
{
    Task PublishAsync(RocketEvent @event, CancellationToken cancellationToken);
}
