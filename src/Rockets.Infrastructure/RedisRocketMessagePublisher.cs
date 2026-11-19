using Rockets.Application;
using Rockets.Domain;
using StackExchange.Redis;

namespace Rockets.Infrastructure;

public sealed class RedisRocketMessagePublisher : IRocketMessagePublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RocketsStreamOptions _options;

    public RedisRocketMessagePublisher(IConnectionMultiplexer redis, RocketsStreamOptions options)
    {
        _redis = redis;
        _options = options;
    }

    public async Task PublishAsync(RocketEvent @event, CancellationToken cancellationToken)
    {
        var (payloadType, payloadJson) = RocketEventPayloadSerializer.Serialize(@event.Payload);
        var db = _redis.GetDatabase();

        await db.StreamAddAsync(_options.StreamKey, new NameValueEntry[]
        {
            new("channel", @event.ChannelId),
            new("messageNumber", @event.MessageNumber),
            new("messageTime", @event.MessageTime.ToString("O")),
            new("payloadType", payloadType),
            new("payloadJson", payloadJson)
        });
    }
}
