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

        _db.RejectedRocketEvents.Add(RejectedRocketEventRecord.Create(
            @event.ChannelId,
            @event.MessageNumber,
            @event.MessageTime,
            payloadType,
            payloadJson,
            reason,
            _timeProvider.GetUtcNow()));

        await _db.SaveChangesAsync(cancellationToken);
    }
}
