namespace Rockets.Api.Tests;

public sealed record RocketApiResponse(
    string Channel,
    string Status,
    string? Type,
    string? Mission,
    int? LaunchSpeed,
    int Speed,
    string? ExplosionReason,
    int LastMessageNumber,
    DateTimeOffset LastMessageTime,
    int SkippedMessageCount,
    bool HasSkippedMessages,
    bool HasLaunchDetails);
