using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class RocketEventRecord
{
    public required string Channel { get; set; }

    public int MessageNumber { get; set; }

    public DateTimeOffset MessageTime { get; set; }

    public DateTimeOffset BufferedAt { get; set; }

    public required string PayloadType { get; set; }

    public required string PayloadJson { get; set; }

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
    public required string Channel { get; set; }

    public int NextMessageNumber { get; set; }
}
