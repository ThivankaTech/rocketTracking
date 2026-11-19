using Rockets.Domain;

namespace Rockets.Application;

public interface IRocketRepository
{
    Task<Result<Rocket?>> FindAsync(string channel, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<Rocket>>> GetAllAsync(CancellationToken cancellationToken);

    Task<Result> SaveAsync(Rocket rocket, CancellationToken cancellationToken);
}
