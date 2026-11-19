using Rockets.Domain;

namespace Rockets.Application;

public sealed class RocketIngestionService
{
    private readonly IRocketEventSequencer _sequencer;
    private readonly IRocketRepository _repository;
    private readonly IRocketChannelLock _channelLock;
    private readonly IRocketRejectedEventStore _rejectedEventStore;

    public RocketIngestionService(
        IRocketEventSequencer sequencer,
        IRocketRepository repository,
        IRocketChannelLock channelLock,
        IRocketRejectedEventStore rejectedEventStore)
    {
        _sequencer = sequencer;
        _repository = repository;
        _channelLock = channelLock;
        _rejectedEventStore = rejectedEventStore;
    }

    public async Task<Result<Rocket?>> HandleAsync(RocketEvent @event, CancellationToken cancellationToken)
    {
        var lockHandle = await _channelLock.AcquireAsync(@event.ChannelId, cancellationToken);
        if (lockHandle is null)
        {
            throw new RocketChannelLockTimeoutException(@event.ChannelId);
        }

        await using (lockHandle)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lockHandle.LockLost);

            return await _sequencer.SubmitAsync(
                @event,
                (released, ct) => ApplyReleasedAsync(@event.ChannelId, released, ct),
                linkedCts.Token);
        }
    }

    public async Task<Result<Rocket?>> RecheckAsync(string channel, CancellationToken cancellationToken)
    {
        var lockHandle = await _channelLock.AcquireAsync(channel, cancellationToken);
        if (lockHandle is null)
        {
            throw new RocketChannelLockTimeoutException(channel);
        }

        await using (lockHandle)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lockHandle.LockLost);

            return await _sequencer.RecheckAsync(
                channel,
                (released, ct) => ApplyReleasedAsync(channel, released, ct),
                linkedCts.Token);
        }
    }

    private async Task<Result<Rocket?>> ApplyReleasedAsync(
        string channel, IReadOnlyList<RocketEvent> releasedInOrder, CancellationToken cancellationToken)
    {
        var currentResult = await _repository.FindAsync(channel, cancellationToken);
        if (currentResult.IsFailure)
        {
            return currentResult;
        }

        var rocket = currentResult.Value;

        if (releasedInOrder.Count == 0)
        {
            return Result<Rocket?>.Success(rocket);
        }

        foreach (var released in releasedInOrder)
        {
            var applyResult = ApplyToAggregate(rocket, released);
            if (applyResult.IsFailure)
            {
                await _rejectedEventStore.RecordAsync(released, applyResult.Error!, cancellationToken);
                continue;
            }

            rocket = applyResult.Value;
        }

        if (rocket is null)
        {
            return Result<Rocket?>.Success(null);
        }

        var saveResult = await _repository.SaveAsync(rocket, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result<Rocket?>.Failure(saveResult.Error!);
        }

        return Result<Rocket?>.Success(rocket);
    }

    private static Result<Rocket> ApplyToAggregate(Rocket? rocket, RocketEvent @event)
    {
        if (rocket is null)
        {
            return Rocket.BeginTracking(@event);
        }

        var applyResult = rocket.Apply(@event);
        return applyResult.IsFailure
            ? Result<Rocket>.Failure(applyResult.Error!)
            : Result<Rocket>.Success(rocket);
    }
}
