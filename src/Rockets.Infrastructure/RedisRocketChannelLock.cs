using Rockets.Application;
using StackExchange.Redis;

namespace Rockets.Infrastructure;

public sealed class RedisRocketChannelLock : IRocketChannelLock
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);

    private readonly IConnectionMultiplexer _redis;

    public RedisRocketChannelLock(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IAsyncDisposable?> AcquireAsync(string channel, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        RedisKey lockKey = $"rocket-lock:{channel}";
        RedisValue lockToken = Guid.NewGuid().ToString("N");

        var deadline = DateTime.UtcNow + AcquireTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await db.LockTakeAsync(lockKey, lockToken, LockDuration))
            {
                return new LockHandle(db, lockKey, lockToken);
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        return null;
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly RedisKey _lockKey;
        private readonly RedisValue _lockToken;

        public LockHandle(IDatabase db, RedisKey lockKey, RedisValue lockToken)
        {
            _db = db;
            _lockKey = lockKey;
            _lockToken = lockToken;
        }

        public async ValueTask DisposeAsync()
        {
            await _db.LockReleaseAsync(_lockKey, _lockToken);
        }
    }
}
