namespace Rockets.Infrastructure;

public sealed class RocketsStreamOptions
{
    public string StreamKey { get; set; } = "rockets:messages";

    public string ConsumerGroup { get; set; } = "rocket-ingestion";

    public TimeSpan ReclaimIdleThreshold { get; set; } = TimeSpan.FromSeconds(30);
}
