namespace Rockets.Domain;

public sealed record RocketEvent(
    string ChannelId,
    int MessageNumber,
    DateTimeOffset MessageTime,
    RocketEventPayload Payload);

public abstract record RocketEventPayload;

public sealed record RocketLaunched(string Type, int LaunchSpeed, string Mission) : RocketEventPayload;

public sealed record RocketSpeedIncreased(int By) : RocketEventPayload;

public sealed record RocketSpeedDecreased(int By) : RocketEventPayload;

public sealed record RocketMissionChanged(string NewMission) : RocketEventPayload;

public sealed record RocketExploded(string Reason) : RocketEventPayload;
