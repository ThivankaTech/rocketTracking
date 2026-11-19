using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rockets.Api.Tests;

public sealed class RocketsApiFactory : WebApplicationFactory<Program>
{
    public readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(2);
    public readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);
    public readonly TimeSpan ReclaimIdleThreshold = TimeSpan.FromSeconds(1);
    public readonly string StreamKey = $"rockets:messages:test:{Guid.NewGuid():N}";
    public readonly string RedisConnectionString = "localhost:6379";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Rockets"] =
                    "Server=localhost,1433;Database=RocketsTest;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True",
                ["ConnectionStrings:Redis"] = RedisConnectionString,
                ["RocketsStream:StreamKey"] = StreamKey,
                ["RocketsStream:ConsumerGroup"] = "rocket-ingestion-test",
                ["RocketsStream:ReclaimIdleThreshold"] = ReclaimIdleThreshold.ToString(),
                ["RocketSequencer:GapTimeout"] = GapTimeout.ToString(),
                ["RocketSequencer:SweepInterval"] = SweepInterval.ToString()
            });
        });
    }
}
