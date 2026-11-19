using System.Runtime.CompilerServices;
using Rockets.Application;
using Rockets.Domain;
using StackExchange.Redis;

namespace Rockets.Infrastructure;

public sealed class RedisRocketMessageConsumer : IRocketMessageConsumer
{
    private static readonly TimeSpan ReclaimCheckInterval = TimeSpan.FromSeconds(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly RocketsStreamOptions _options;
    private readonly string _consumerName;
    private DateTime _lastReclaimAttempt = DateTime.MinValue;

    public RedisRocketMessageConsumer(IConnectionMultiplexer redis, RocketsStreamOptions options)
    {
        _redis = redis;
        _options = options;
        _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    public async IAsyncEnumerable<RocketMessageDelivery> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await EnsureConsumerGroupAsync(db);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow - _lastReclaimAttempt >= ReclaimCheckInterval)
            {
                _lastReclaimAttempt = DateTime.UtcNow;

                foreach (var reclaimed in await ReclaimStaleEntriesAsync(db))
                {
                    yield return reclaimed;
                }
            }

            var entries = await db.StreamReadGroupAsync(
                _options.StreamKey, _options.ConsumerGroup, _consumerName, position: ">", count: 50);

            if (entries.Length == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                continue;
            }

            foreach (var entry in entries)
            {
                yield return ToDelivery(entry);
            }
        }
    }

    public Task AcknowledgeAsync(string deliveryToken, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        return db.StreamAcknowledgeAsync(_options.StreamKey, _options.ConsumerGroup, deliveryToken);
    }

    private async Task<IReadOnlyList<RocketMessageDelivery>> ReclaimStaleEntriesAsync(IDatabase db)
    {
        var result = await db.StreamAutoClaimAsync(
            _options.StreamKey,
            _options.ConsumerGroup,
            _consumerName,
            (long)_options.ReclaimIdleThreshold.TotalMilliseconds,
            "0-0",
            count: 50);

        return result.ClaimedEntries.Select(ToDelivery).ToList();
    }

    private async Task EnsureConsumerGroupAsync(IDatabase db)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                _options.StreamKey, _options.ConsumerGroup, StreamPosition.NewMessages, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
        }
    }

    private static RocketMessageDelivery ToDelivery(StreamEntry entry)
    {
        var fields = entry.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString());

        var @event = new RocketEvent(
            fields["channel"],
            int.Parse(fields["messageNumber"]),
            DateTimeOffset.Parse(fields["messageTime"]),
            RocketEventPayloadSerializer.Deserialize(fields["payloadType"], fields["payloadJson"]));

        return new RocketMessageDelivery(entry.Id.ToString(), @event);
    }
}
