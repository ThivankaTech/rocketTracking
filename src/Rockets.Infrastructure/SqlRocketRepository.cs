using Microsoft.EntityFrameworkCore;
using Rockets.Application;
using Rockets.Domain;

namespace Rockets.Infrastructure;

public sealed class SqlRocketRepository : IRocketRepository
{
    private readonly RocketsDbContext _db;

    public SqlRocketRepository(RocketsDbContext db)
    {
        _db = db;
    }

    public async Task<Result<Rocket?>> FindAsync(string channel, CancellationToken cancellationToken)
    {
        var snapshot = await _db.Rockets.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Channel == channel, cancellationToken);

        return Result<Rocket?>.Success(snapshot?.ToDomain());
    }

    public async Task<Result<IReadOnlyList<Rocket>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _db.Rockets.AsNoTracking().ToListAsync(cancellationToken);

        IReadOnlyList<Rocket> rockets = snapshots.Select(s => s.ToDomain()).ToList();

        return Result<IReadOnlyList<Rocket>>.Success(rockets);
    }

    public async Task<Result> SaveAsync(Rocket rocket, CancellationToken cancellationToken)
    {
        var snapshot = await _db.Rockets.SingleOrDefaultAsync(r => r.Channel == rocket.ChannelId, cancellationToken);

        if (snapshot is null)
        {
            snapshot = new RocketSnapshotRecord { Channel = rocket.ChannelId };
            snapshot.CopyFrom(rocket);
            _db.Rockets.Add(snapshot);
        }
        else
        {
            snapshot.CopyFrom(rocket);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
