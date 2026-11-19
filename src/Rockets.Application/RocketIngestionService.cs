using Rockets.Domain;

namespace Rockets.Application;

public sealed class RocketIngestionService
{
    private readonly IRocketEventSequencer _sequencer;
    private readonly IRocketRepository _repository;
    private readonly IRocketChannelLock _channelLock;

    public RocketIngestionService(
        IRocketEventSequencer sequencer, IRocketRepository repository, IRocketChannelLock channelLock)
    {
        _sequencer = sequencer;
        _repository = repository;
        _channelLock = channelLock;
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
            var sequenceResult = await _sequencer.SubmitAsync(@event, cancellationToken);
            if (sequenceResult.IsFailure)
            {
                return Result<Rocket?>.Failure(sequenceResult.Error!);
            }

            return await ApplyReleasedAsync(@event.ChannelId, sequenceResult.Value, cancellationToken);
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
            var sequenceResult = await _sequencer.RecheckAsync(channel, cancellationToken);
            if (sequenceResult.IsFailure)
            {
                return Result<Rocket?>.Failure(sequenceResult.Error!);
            }

            return await ApplyReleasedAsync(channel, sequenceResult.Value, cancellationToken);
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
                return Result<Rocket?>.Failure(applyResult.Error!);
            }

            rocket = applyResult.Value;
        }

        var saveResult = await _repository.SaveAsync(rocket!, cancellationToken);
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
