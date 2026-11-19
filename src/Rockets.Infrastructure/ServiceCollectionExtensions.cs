using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rockets.Application;
using StackExchange.Redis;

namespace Rockets.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRocketsInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<RocketsDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var sqlConnectionString = configuration.GetConnectionString("Rockets")
                ?? throw new InvalidOperationException("Connection string 'Rockets' is not configured.");

            options.UseSqlServer(sqlConnectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var redisConnectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
            return ConnectionMultiplexer.Connect(redisConnectionString);
        });

        services.AddSingleton(provider => BindStreamOptions(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(provider => BindSequencerOptions(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IRocketEventSequencer, SqlRocketEventSequencer>();
        services.AddScoped<IRocketRepository, SqlRocketRepository>();
        services.AddScoped<RocketIngestionService>();
        services.AddScoped<RocketQueryService>();
        services.AddScoped<RocketMessageGateway>();

        services.AddSingleton<IRocketMessagePublisher, RedisRocketMessagePublisher>();
        services.AddSingleton<IRocketMessageConsumer, RedisRocketMessageConsumer>();
        services.AddSingleton<IRocketChannelLock, RedisRocketChannelLock>();

        services.AddHostedService<RocketStreamProcessingService>();
        services.AddHostedService<RocketGapSweepService>();

        return services;
    }

    private static RocketSequencerOptions BindSequencerOptions(IConfiguration configuration)
    {
        var options = new RocketSequencerOptions();
        configuration.GetSection("RocketSequencer").Bind(options);
        return options;
    }

    private static RocketsStreamOptions BindStreamOptions(IConfiguration configuration)
    {
        var options = new RocketsStreamOptions();
        configuration.GetSection("RocketsStream").Bind(options);
        return options;
    }

    public static void EnsureRocketsInfrastructureReady(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RocketsDbContext>();
        db.Database.EnsureCreated();
    }
}
