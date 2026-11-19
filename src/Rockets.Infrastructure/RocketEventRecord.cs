using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class RocketEventRecord
{
    public required string Channel { get; init; }

    public int MessageNumber { get; init; }

    public DateTimeOffset MessageTime { get; init; }

    public DateTimeOffset BufferedAt { get; init; }

    public required string PayloadType { get; init; }

    public required string PayloadJson { get; init; }

    public static RocketEventRecord FromDomain(RocketEvent @event, DateTimeOffset bufferedAt)
    {
        var (payloadType, payloadJson) = RocketEventPayloadSerializer.Serialize(@event.Payload);

        return new RocketEventRecord
        {
            Channel = @event.ChannelId,
            MessageNumber = @event.MessageNumber,
            MessageTime = @event.MessageTime,
            BufferedAt = bufferedAt,
            PayloadType = payloadType,
            PayloadJson = payloadJson
        };
    }

    public RocketEvent ToDomain() =>
        new(Channel, MessageNumber, MessageTime, RocketEventPayloadSerializer.Deserialize(PayloadType, PayloadJson));
}

public sealed class RocketChannelCursorRecord
{
    private RocketChannelCursorRecord(string channel, int nextMessageNumber)
    {
        Channel = channel;
        NextMessageNumber = nextMessageNumber;
    }

    public string Channel { get; }

    public int NextMessageNumber { get; private set; }

    public static RocketChannelCursorRecord Create(string channel, int nextMessageNumber) =>
        new(channel, nextMessageNumber);

    public void AdvanceTo(int nextMessageNumber)
    {
        if (nextMessageNumber <= NextMessageNumber)
        {
            throw new InvalidOperationException(
                $"Cursor for channel {Channel} cannot move backwards from {NextMessageNumber} to {nextMessageNumber}; " +
                "already-released messages would be re-applied.");
        }

        NextMessageNumber = nextMessageNumber;
    }
}
