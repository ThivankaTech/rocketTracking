using Rockets.Domain;

namespace Rockets.Application;

public sealed class RocketMessageGateway
{
    private readonly IRocketMessagePublisher _publisher;

    public RocketMessageGateway(IRocketMessagePublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<Result> SubmitAsync(
        string channel,
        int messageNumber,
        DateTimeOffset messageTime,
        string messageType,
        string messageJson,
        CancellationToken cancellationToken)
    {
        var payloadResult = RocketEventPayloadParser.Parse(messageType, messageJson);
        if (payloadResult.IsFailure)
        {
            return Result.Failure(payloadResult.Error!);
        }

        var @event = new RocketEvent(channel, messageNumber, messageTime, payloadResult.Value);
        await _publisher.PublishAsync(@event, cancellationToken);

        return Result.Success();
    }
}
