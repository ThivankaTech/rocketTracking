namespace Rockets.Domain;

public sealed class Rocket
{
    private Rocket(string channelId)
    {
        ChannelId = channelId;
        Status = RocketStatus.InFlight;
    }

    private Rocket(
        string channelId,
        RocketStatus status,
        string? type,
        string? mission,
        int? launchSpeed,
        int speed,
        string? explosionReason,
        int lastMessageNumber,
        DateTimeOffset lastMessageTime,
        int skippedMessageCount)
    {
        ChannelId = channelId;
        Status = status;
        Type = type;
        Mission = mission;
        LaunchSpeed = launchSpeed;
        Speed = speed;
        ExplosionReason = explosionReason;
        LastMessageNumber = lastMessageNumber;
        LastMessageTime = lastMessageTime;
        SkippedMessageCount = skippedMessageCount;
    }

    public string ChannelId { get; }

    public RocketStatus Status { get; private set; }

    public string? Type { get; private set; }

    public string? Mission { get; private set; }

    public int? LaunchSpeed { get; private set; }

    public int Speed { get; private set; }

    public string? ExplosionReason { get; private set; }

    public int LastMessageNumber { get; private set; }

    public DateTimeOffset LastMessageTime { get; private set; }

    public int SkippedMessageCount { get; private set; }

    public bool IsFlying => Status == RocketStatus.InFlight;

    public bool HasExploded => Status == RocketStatus.Exploded;

    public bool HasSkippedMessages => SkippedMessageCount > 0;

    public bool HasLaunchDetails => Type is not null;

    public static Result<Rocket> BeginTracking(RocketEvent @event)
    {
        var rocket = new Rocket(@event.ChannelId);

        var firstMessage = rocket.Apply(@event);

        return firstMessage.IsFailure
            ? Result<Rocket>.Failure(firstMessage.Error!)
            : Result<Rocket>.Success(rocket);
    }

    public static Rocket Restore(
        string channelId,
        RocketStatus status,
        string? type,
        string? mission,
        int? launchSpeed,
        int speed,
        string? explosionReason,
        int lastMessageNumber,
        DateTimeOffset lastMessageTime,
        int skippedMessageCount) =>
        new(channelId, status, type, mission, launchSpeed, speed, explosionReason, lastMessageNumber, lastMessageTime,
            skippedMessageCount);

    public Result Apply(RocketEvent @event)
    {
        var belongsHere = EnsureEventBelongsToThisRocket(@event);
        if (belongsHere.IsFailure)
        {
            return belongsHere;
        }

        var movesForward = EnsureEventIsNewerThanLastProcessed(@event);
        if (movesForward.IsFailure)
        {
            return movesForward;
        }

        RecordMessagesGivenUpOnBefore(@event.MessageNumber);

        if (IsFlying)
        {
            var stateChange = ChangeStateFrom(@event.Payload);
            if (stateChange.IsFailure)
            {
                return stateChange;
            }
        }

        RecordMessageProcessed(@event);
        return Result.Success();
    }

    private Result EnsureEventBelongsToThisRocket(RocketEvent @event) =>
        @event.ChannelId == ChannelId
            ? Result.Success()
            : Result.Failure(
                $"Message for channel {@event.ChannelId} cannot be applied to rocket {ChannelId}.");

    private Result EnsureEventIsNewerThanLastProcessed(RocketEvent @event) =>
        @event.MessageNumber > LastMessageNumber
            ? Result.Success()
            : Result.Failure(
                $"Rocket {ChannelId} already processed up to message {LastMessageNumber}, " +
                $"so message {@event.MessageNumber} is a duplicate or arrived too late.");

    private Result ChangeStateFrom(RocketEventPayload payload)
    {
        switch (payload)
        {
            case RocketLaunched launched:
                if (HasLaunchDetails)
                {
                    return Result.Failure($"Rocket {ChannelId} has already launched.");
                }

                RecordLaunchDetails(launched);
                break;

            case RocketSpeedIncreased increased:
                IncreaseSpeed(increased.By);
                break;

            case RocketSpeedDecreased decreased:
                DecreaseSpeed(decreased.By);
                break;

            case RocketMissionChanged changed:
                ChangeMission(changed.NewMission);
                break;

            case RocketExploded exploded:
                Explode(exploded.Reason);
                break;

            default:
                return Result.Failure($"Rocket {ChannelId} does not know how to handle {payload.GetType().Name}.");
        }

        return Result.Success();
    }

    private void RecordLaunchDetails(RocketLaunched launched)
    {
        Type = launched.Type;
        Mission ??= launched.Mission;
        LaunchSpeed = launched.LaunchSpeed;
        Speed += launched.LaunchSpeed;
    }

    private void IncreaseSpeed(int by) => Speed += by;

    private void DecreaseSpeed(int by) => Speed -= by;

    private void ChangeMission(string newMission) => Mission = newMission;

    private void Explode(string reason)
    {
        ExplosionReason = reason;
        Status = RocketStatus.Exploded;
    }

    private void RecordMessagesGivenUpOnBefore(int messageNumber) =>
        SkippedMessageCount += messageNumber - LastMessageNumber - 1;

    private void RecordMessageProcessed(RocketEvent @event)
    {
        LastMessageNumber = @event.MessageNumber;
        LastMessageTime = @event.MessageTime;
    }
}
