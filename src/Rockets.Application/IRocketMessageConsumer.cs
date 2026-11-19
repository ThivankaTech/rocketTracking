using Rockets.Domain;

namespace Rockets.Application;

public sealed record RocketMessageDelivery(string DeliveryToken, RocketEvent Event);

public interface IRocketMessageConsumer
{
    IAsyncEnumerable<RocketMessageDelivery> ConsumeAsync(CancellationToken cancellationToken);

    Task AcknowledgeAsync(string deliveryToken, CancellationToken cancellationToken);
}
