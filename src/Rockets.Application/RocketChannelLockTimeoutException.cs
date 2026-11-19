namespace Rockets.Application;

public sealed class RocketChannelLockTimeoutException : Exception
{
    public RocketChannelLockTimeoutException(string channel)
        : base($"Could not acquire the channel lock for '{channel}' within the retry window.")
    {
    }
}
