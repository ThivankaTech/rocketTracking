namespace Rockets.Application;

public sealed class RocketSequencerOptions
{
    public TimeSpan GapTimeout { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);
}
