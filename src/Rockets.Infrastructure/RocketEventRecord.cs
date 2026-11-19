using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class RocketEventRecord
{
    public string Channel { get; set; } = string.Empty;

    public int MessageNumber { get; set; }

    public DateTimeOffset MessageTime { get; set; }

    public DateTimeOffset BufferedAt { get; set; }

    public string PayloadType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

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
    public string Channel { get; set; } = string.Empty;

    public int NextMessageNumber { get; set; }
}
