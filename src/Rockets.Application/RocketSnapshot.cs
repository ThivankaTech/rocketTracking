using Rockets.Domain;

namespace Rockets.Application;

public sealed record RocketSnapshot(
    string Channel,
    RocketStatus Status,
    string? Type,
    string? Mission,
    int? LaunchSpeed,
    int Speed,
    string? ExplosionReason,
    int LastMessageNumber,
    DateTimeOffset LastMessageTime,
    int SkippedMessageCount,
    bool HasSkippedMessages,
    bool HasLaunchDetails)
{
    public static RocketSnapshot FromDomain(Rocket rocket) => new(
        rocket.ChannelId,
        rocket.Status,
        rocket.Type,
        rocket.Mission,
        rocket.LaunchSpeed,
        rocket.Speed,
        rocket.ExplosionReason,
        rocket.LastMessageNumber,
        rocket.LastMessageTime,
        rocket.SkippedMessageCount,
        rocket.HasSkippedMessages,
        rocket.HasLaunchDetails);
}
