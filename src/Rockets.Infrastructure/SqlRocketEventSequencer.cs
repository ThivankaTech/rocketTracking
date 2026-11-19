using System.Data;
using Microsoft.EntityFrameworkCore;
using Rockets.Application;
using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class SqlRocketEventSequencer : IRocketEventSequencer
{
    private readonly RocketsDbContext _db;
    private readonly RocketSequencerOptions _options;
    private readonly TimeProvider _timeProvider;

    public SqlRocketEventSequencer(
        RocketsDbContext db, RocketSequencerOptions options, TimeProvider timeProvider)
    {
        _db = db;
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task<Result<IReadOnlyList<RocketEvent>>> SubmitAsync(
        RocketEvent @event, CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(() => SubmitOnceAsync(@event, cancellationToken));
    }

    public Task<Result<IReadOnlyList<RocketEvent>>> RecheckAsync(string channel, CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(() => RecheckOnceAsync(channel, cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetStaleChannelsWithBacklogAsync(CancellationToken cancellationToken)
    {
        var nextMessageNumberByChannel = await _db.RocketChannelCursors.AsNoTracking()
            .ToDictionaryAsync(c => c.Channel, c => c.NextMessageNumber, cancellationToken);

        var bufferedEvents = await _db.RocketEvents.AsNoTracking()
            .Select(e => new { e.Channel, e.MessageNumber, e.BufferedAt })
            .ToListAsync(cancellationToken);

        var cutoff = _timeProvider.GetUtcNow() - _options.GapTimeout;
        var staleChannels = new List<string>();

        foreach (var channelEvents in bufferedEvents.GroupBy(e => e.Channel))
        {
            var channel = channelEvents.Key;
            var nextMessageNumber = nextMessageNumberByChannel.GetValueOrDefault(channel, 1);

            var backlog = channelEvents.Where(e => e.MessageNumber >= nextMessageNumber).ToList();
            if (backlog.Count == 0)
            {
                continue;
            }

            var latestActivity = backlog.Max(e => e.BufferedAt);
            var hasGoneQuiet = latestActivity <= cutoff;

            if (hasGoneQuiet)
            {
                staleChannels.Add(channel);
            }
        }

        return staleChannels;
    }

    private async Task<Result<IReadOnlyList<RocketEvent>>> SubmitOnceAsync(
        RocketEvent @event, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var cursor = await _db.RocketChannelCursors
            .SingleOrDefaultAsync(c => c.Channel == @event.ChannelId, cancellationToken);

        var nextMessageNumber = cursor?.NextMessageNumber ?? 1;

        if (@event.MessageNumber < nextMessageNumber)
        {
            await transaction.CommitAsync(cancellationToken);
            return Result<IReadOnlyList<RocketEvent>>.Success(Array.Empty<RocketEvent>());
        }

        await BufferAsync(@event, cancellationToken);

        var released = await ReleaseAsync(@event.ChannelId, cursor, nextMessageNumber, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Result<IReadOnlyList<RocketEvent>>.Success(released);
    }

    private async Task<Result<IReadOnlyList<RocketEvent>>> RecheckOnceAsync(
        string channel, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var cursor = await _db.RocketChannelCursors
            .SingleOrDefaultAsync(c => c.Channel == channel, cancellationToken);

        var nextMessageNumber = cursor?.NextMessageNumber ?? 1;

        var released = await ReleaseAsync(channel, cursor, nextMessageNumber, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Result<IReadOnlyList<RocketEvent>>.Success(released);
    }

    private async Task<IReadOnlyList<RocketEvent>> ReleaseAsync(
        string channel, RocketChannelCursorRecord? cursor, int nextMessageNumber, CancellationToken cancellationToken)
    {
        var released = await ReleaseContiguousAsync(channel, nextMessageNumber, cancellationToken);

        if (released.Count == 0)
        {
            released = await ReleaseTimedOutAsync(channel, nextMessageNumber, cancellationToken);
        }

        if (released.Count > 0)
        {
            AdvanceCursor(channel, cursor, released[^1].MessageNumber + 1);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return released;
    }

    private async Task BufferAsync(RocketEvent @event, CancellationToken cancellationToken)
    {
        var alreadyBuffered = await _db.RocketEvents.AnyAsync(
            e => e.Channel == @event.ChannelId && e.MessageNumber == @event.MessageNumber,
            cancellationToken);

        if (alreadyBuffered)
        {
            return;
        }

        _db.RocketEvents.Add(RocketEventRecord.FromDomain(@event, _timeProvider.GetUtcNow()));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RocketEvent>> ReleaseContiguousAsync(
        string channel, int fromMessageNumber, CancellationToken cancellationToken)
    {
        var bufferedNumbers = await _db.RocketEvents.AsNoTracking()
            .Where(e => e.Channel == channel && e.MessageNumber >= fromMessageNumber)
            .OrderBy(e => e.MessageNumber)
            .Select(e => e.MessageNumber)
            .ToListAsync(cancellationToken);

        var contiguousCount = CountContiguousFrom(bufferedNumbers, fromMessageNumber);

        if (contiguousCount == 0)
        {
            return Array.Empty<RocketEvent>();
        }

        var toMessageNumber = fromMessageNumber + contiguousCount - 1;

        var released = await _db.RocketEvents.AsNoTracking()
            .Where(e => e.Channel == channel
                        && e.MessageNumber >= fromMessageNumber
                        && e.MessageNumber <= toMessageNumber)
            .OrderBy(e => e.MessageNumber)
            .ToListAsync(cancellationToken);

        return released.Select(e => e.ToDomain()).ToList();
    }

    private async Task<IReadOnlyList<RocketEvent>> ReleaseTimedOutAsync(
        string channel, int fromMessageNumber, CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow() - _options.GapTimeout;

        var timedOut = await _db.RocketEvents.AsNoTracking()
            .Where(e => e.Channel == channel
                        && e.MessageNumber >= fromMessageNumber
                        && e.BufferedAt <= cutoff)
            .OrderBy(e => e.MessageNumber)
            .ToListAsync(cancellationToken);

        return timedOut.Select(e => e.ToDomain()).ToList();
    }

    private static int CountContiguousFrom(IReadOnlyList<int> orderedMessageNumbers, int fromMessageNumber)
    {
        var count = 0;

        foreach (var messageNumber in orderedMessageNumbers)
        {
            if (messageNumber != fromMessageNumber + count)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private void AdvanceCursor(string channel, RocketChannelCursorRecord? cursor, int nextMessageNumber)
    {
        if (cursor is null)
        {
            _db.RocketChannelCursors.Add(new RocketChannelCursorRecord
            {
                Channel = channel,
                NextMessageNumber = nextMessageNumber
            });
        }
        else
        {
            cursor.NextMessageNumber = nextMessageNumber;
        }
    }
}
