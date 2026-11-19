using Microsoft.Extensions.Logging;
using Rockets.Application;
using StackExchange.Redis;

namespace Rockets.Infrastructure;

public sealed class RedisRocketChannelLock : IRocketChannelLock
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(10);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRocketChannelLock> _logger;

    public RedisRocketChannelLock(IConnectionMultiplexer redis, ILogger<RedisRocketChannelLock> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IRocketChannelLockHandle?> AcquireAsync(string channel, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        RedisKey lockKey = $"rocket-lock:{channel}";
        RedisValue lockToken = Guid.NewGuid().ToString("N");

        var deadline = DateTime.UtcNow + AcquireTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await db.LockTakeAsync(lockKey, lockToken, LockDuration))
            {
                return new LockHandle(db, lockKey, lockToken, _logger);
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        return null;
    }

    private sealed class LockHandle : IRocketChannelLockHandle
    {
        private readonly IDatabase _db;
        private readonly RedisKey _lockKey;
        private readonly RedisValue _lockToken;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _lockLostSource = new();
        private readonly CancellationTokenSource _stopRenewalSource = new();
        private readonly Task _renewalLoop;

        public LockHandle(IDatabase db, RedisKey lockKey, RedisValue lockToken, ILogger logger)
        {
            _db = db;
            _lockKey = lockKey;
            _lockToken = lockToken;
            _logger = logger;
            _renewalLoop = RenewPeriodicallyAsync();
        }

        public CancellationToken LockLost => _lockLostSource.Token;

        private async Task RenewPeriodicallyAsync()
        {
            try
            {
                while (!_stopRenewalSource.IsCancellationRequested)
                {
                    await Task.Delay(RenewalInterval, _stopRenewalSource.Token);

                    var renewed = await _db.LockExtendAsync(_lockKey, _lockToken, LockDuration);
                    if (!renewed)
                    {
                        _logger.LogError(
                            "Lost channel lock {LockKey}; cancelling in-flight work for this channel.", _lockKey);
                        _lockLostSource.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Channel lock renewal failed for {LockKey}; cancelling in-flight work.", _lockKey);
                _lockLostSource.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stopRenewalSource.Cancel();

            try
            {
                await _renewalLoop;
            }
            catch
            {
                // The renewal loop already logs its own failures; nothing further to surface here.
            }

            _stopRenewalSource.Dispose();
            _lockLostSource.Dispose();

            await _db.LockReleaseAsync(_lockKey, _lockToken);
        }
    }
}
