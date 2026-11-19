namespace Rockets.Application;

public interface IRocketChannelLock
{
    Task<IAsyncDisposable?> AcquireAsync(string channel, CancellationToken cancellationToken);
}
