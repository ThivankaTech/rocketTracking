using Microsoft.Extensions.Logging.Abstractions;
using Rockets.Infrastructure;
using StackExchange.Redis;

namespace Rockets.Api.Tests;

public sealed class RedisRocketChannelLockTests
{
    [Fact]
    public async Task HeldLock_IsRenewedBeforeItsOriginalTtlWouldHaveExpired()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
        var db = redis.GetDatabase();
        var channel = $"lock-test-{Guid.NewGuid():N}";
        RedisKey lockKey = $"rocket-lock:{channel}";

        var channelLock = new RedisRocketChannelLock(redis, NullLogger<RedisRocketChannelLock>.Instance);

        var handle = await channelLock.AcquireAsync(channel, CancellationToken.None);
        Assert.NotNull(handle);

        await using (handle)
        {
            var initialTtl = await db.KeyTimeToLiveAsync(lockKey);
            Assert.NotNull(initialTtl);
            Assert.True(initialTtl.Value > TimeSpan.FromSeconds(20));

            // Wait past the renewal interval (10s) but well short of the original 30s expiry -
            // without a heartbeat, the TTL would simply keep ticking down toward zero.
            await Task.Delay(TimeSpan.FromSeconds(11));

            var ttlAfterRenewal = await db.KeyTimeToLiveAsync(lockKey);
            Assert.NotNull(ttlAfterRenewal);
            Assert.True(ttlAfterRenewal.Value > TimeSpan.FromSeconds(20));

            Assert.False(handle!.LockLost.IsCancellationRequested);
        }

        var existsAfterRelease = await db.KeyExistsAsync(lockKey);
        Assert.False(existsAfterRelease);
    }
}
