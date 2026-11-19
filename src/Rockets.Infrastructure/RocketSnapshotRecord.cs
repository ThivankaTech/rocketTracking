using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class RocketSnapshotRecord
{
    public string Channel { get; set; } = string.Empty;

    public RocketStatus Status { get; set; }

    public string? Type { get; set; }

    public string? Mission { get; set; }

    public int? LaunchSpeed { get; set; }

    public int Speed { get; set; }

    public string? ExplosionReason { get; set; }

    public int LastMessageNumber { get; set; }

    public DateTimeOffset LastMessageTime { get; set; }

    public int SkippedMessageCount { get; set; }

    public void CopyFrom(Rocket rocket)
    {
        Channel = rocket.ChannelId;
        Status = rocket.Status;
        Type = rocket.Type;
        Mission = rocket.Mission;
        LaunchSpeed = rocket.LaunchSpeed;
        Speed = rocket.Speed;
        ExplosionReason = rocket.ExplosionReason;
        LastMessageNumber = rocket.LastMessageNumber;
        LastMessageTime = rocket.LastMessageTime;
        SkippedMessageCount = rocket.SkippedMessageCount;
    }

    public Rocket ToDomain() => Rocket.Restore(
        Channel, Status, Type, Mission, LaunchSpeed, Speed, ExplosionReason, LastMessageNumber, LastMessageTime,
        SkippedMessageCount);
}
