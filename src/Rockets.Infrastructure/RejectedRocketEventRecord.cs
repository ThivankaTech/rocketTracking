namespace Rockets.Infrastructure;

public sealed class RejectedRocketEventRecord
{
    private RejectedRocketEventRecord(
        string channel,
        int messageNumber,
        DateTimeOffset messageTime,
        string payloadType,
        string payloadJson,
        string reason,
        DateTimeOffset rejectedAt)
    {
        Channel = channel;
        MessageNumber = messageNumber;
        MessageTime = messageTime;
        PayloadType = payloadType;
        PayloadJson = payloadJson;
        Reason = reason;
        RejectedAt = rejectedAt;
    }

    public int Id { get; private set; }

    public string Channel { get; }

    public int MessageNumber { get; }

    public DateTimeOffset MessageTime { get; }

    public string PayloadType { get; }

    public string PayloadJson { get; }

    public string Reason { get; }

    public DateTimeOffset RejectedAt { get; }

    public static RejectedRocketEventRecord Create(
        string channel,
        int messageNumber,
        DateTimeOffset messageTime,
        string payloadType,
        string payloadJson,
        string reason,
        DateTimeOffset rejectedAt) =>
        new(channel, messageNumber, messageTime, payloadType, payloadJson, reason, rejectedAt);
}
