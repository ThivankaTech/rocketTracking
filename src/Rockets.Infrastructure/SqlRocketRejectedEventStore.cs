using Rockets.Application;
using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class SqlRocketRejectedEventStore : IRocketRejectedEventStore
{
    private readonly RocketsDbContext _db;
    private readonly TimeProvider _timeProvider;

    public SqlRocketRejectedEventStore(RocketsDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task RecordAsync(RocketEvent @event, string reason, CancellationToken cancellationToken)
    {
        var (payloadType, payloadJson) = RocketEventPayloadSerializer.Serialize(@event.Payload);

        _db.RejectedRocketEvents.Add(new RejectedRocketEventRecord
        {
            Channel = @event.ChannelId,
            MessageNumber = @event.MessageNumber,
            MessageTime = @event.MessageTime,
            PayloadType = payloadType,
            PayloadJson = payloadJson,
            Reason = reason,
            RejectedAt = _timeProvider.GetUtcNow()
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
