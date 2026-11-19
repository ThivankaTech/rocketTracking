namespace Rockets.Application;

public interface IRocketChannelLock
{
    Task<IRocketChannelLockHandle?> AcquireAsync(string channel, CancellationToken cancellationToken);
}

public interface IRocketChannelLockHandle : IAsyncDisposable
{
    CancellationToken LockLost { get; }
}
