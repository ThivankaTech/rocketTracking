using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class RocketSnapshotRecord
{
    private RocketSnapshotRecord(string channel)
    {
        Channel = channel;
    }

    public string Channel { get; }

    public RocketStatus Status { get; private set; }

    public string? Type { get; private set; }

    public string? Mission { get; private set; }

    public int? LaunchSpeed { get; private set; }

    public int Speed { get; private set; }

    public string? ExplosionReason { get; private set; }

    public int LastMessageNumber { get; private set; }

    public DateTimeOffset LastMessageTime { get; private set; }

    public int SkippedMessageCount { get; private set; }

    public void CopyFrom(Rocket rocket)
    {
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

    public static RocketSnapshotRecord Create(Rocket rocket)
    {
        var snapshot = new RocketSnapshotRecord(rocket.ChannelId);
        snapshot.CopyFrom(rocket);
        return snapshot;
    }

    public Rocket ToDomain() => Rocket.Restore(
        Channel, Status, Type, Mission, LaunchSpeed, Speed, ExplosionReason, LastMessageNumber, LastMessageTime,
        SkippedMessageCount);
}
