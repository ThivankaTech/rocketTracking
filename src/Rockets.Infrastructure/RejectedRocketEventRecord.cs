namespace Rockets.Infrastructure;

public sealed class RejectedRocketEventRecord
{
    public int Id { get; set; }

    public required string Channel { get; set; }

    public int MessageNumber { get; set; }

    public DateTimeOffset MessageTime { get; set; }

    public required string PayloadType { get; set; }

    public required string PayloadJson { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset RejectedAt { get; set; }
}
