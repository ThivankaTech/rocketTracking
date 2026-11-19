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

    public Task<Result<Rocket?>> SubmitAsync(
        RocketEvent @event,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(() => SubmitOnceAsync(@event, applyReleasedAsync, cancellationToken));
    }

    public Task<Result<Rocket?>> RecheckAsync(
        string channel,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(() => RecheckOnceAsync(channel, applyReleasedAsync, cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetStaleChannelsWithBacklogAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow() - _options.GapTimeout;

        return await _db.RocketEvents.AsNoTracking()
            .GroupJoin(
                _db.RocketChannelCursors.AsNoTracking(),
                rocketEvent => rocketEvent.Channel,
                cursor => cursor.Channel,
                (rocketEvent, cursors) => new { rocketEvent, cursors })
            .SelectMany(
                x => x.cursors.DefaultIfEmpty(),
                (x, cursor) => new
                {
                    x.rocketEvent.Channel,
                    x.rocketEvent.MessageNumber,
                    x.rocketEvent.BufferedAt,
                    NextMessageNumber = cursor == null ? 1 : cursor.NextMessageNumber
                })
            .Where(x => x.MessageNumber >= x.NextMessageNumber)
            .GroupBy(x => x.Channel)
            .Where(g => g.Max(x => x.BufferedAt) <= cutoff)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);
    }

    private async Task<Result<Rocket?>> SubmitOnceAsync(
        RocketEvent @event,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var cursor = await _db.RocketChannelCursors
            .SingleOrDefaultAsync(c => c.Channel == @event.ChannelId, cancellationToken);

        var nextMessageNumber = cursor?.NextMessageNumber ?? 1;

        IReadOnlyList<RocketEvent> released = Array.Empty<RocketEvent>();

        if (@event.MessageNumber >= nextMessageNumber)
        {
            await BufferAsync(@event, cancellationToken);
            released = await ReleaseAsync(@event.ChannelId, cursor, nextMessageNumber, cancellationToken);
        }

        var applyResult = await applyReleasedAsync(released, cancellationToken);
        if (applyResult.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return applyResult;
        }

        await transaction.CommitAsync(cancellationToken);

        return applyResult;
    }

    private async Task<Result<Rocket?>> RecheckOnceAsync(
        string channel,
        Func<IReadOnlyList<RocketEvent>, CancellationToken, Task<Result<Rocket?>>> applyReleasedAsync,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var cursor = await _db.RocketChannelCursors
            .SingleOrDefaultAsync(c => c.Channel == channel, cancellationToken);

        var nextMessageNumber = cursor?.NextMessageNumber ?? 1;

        var released = await ReleaseAsync(channel, cursor, nextMessageNumber, cancellationToken);

        var applyResult = await applyReleasedAsync(released, cancellationToken);
        if (applyResult.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return applyResult;
        }

        await transaction.CommitAsync(cancellationToken);

        return applyResult;
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
            var releasedMessageNumbers = released.Select(e => e.MessageNumber).ToList();

            await _db.RocketEvents
                .Where(e => e.Channel == channel && releasedMessageNumbers.Contains(e.MessageNumber))
                .ExecuteDeleteAsync(cancellationToken);

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
