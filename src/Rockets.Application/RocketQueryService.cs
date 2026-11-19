using Rockets.Domain;

namespace Rockets.Application;

public sealed class RocketQueryService
{
    private readonly IRocketRepository _repository;

    public RocketQueryService(IRocketRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<RocketSnapshot>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = await _repository.GetAllAsync(cancellationToken);
        if (result.IsFailure)
        {
            return Result<IReadOnlyList<RocketSnapshot>>.Failure(result.Error!);
        }

        IReadOnlyList<RocketSnapshot> ordered = result.Value
            .OrderByDescending(r => r.Speed)
            .Select(RocketSnapshot.FromDomain)
            .ToList();

        return Result<IReadOnlyList<RocketSnapshot>>.Success(ordered);
    }

    public async Task<Result<RocketSnapshot?>> FindAsync(string channel, CancellationToken cancellationToken)
    {
        var result = await _repository.FindAsync(channel, cancellationToken);
        if (result.IsFailure)
        {
            return Result<RocketSnapshot?>.Failure(result.Error!);
        }

        return Result<RocketSnapshot?>.Success(result.Value is { } rocket ? RocketSnapshot.FromDomain(rocket) : null);
    }
}
