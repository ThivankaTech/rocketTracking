using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rockets.Api.Tests;

public sealed class RocketsApiFactory : WebApplicationFactory<Program>
{
    public readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(2);
    public readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Rockets"] =
                    "Server=localhost,1433;Database=RocketsTest;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["RocketsStream:StreamKey"] = $"rockets:messages:test:{Guid.NewGuid():N}",
                ["RocketsStream:ConsumerGroup"] = "rocket-ingestion-test",
                ["RocketSequencer:GapTimeout"] = GapTimeout.ToString(),
                ["RocketSequencer:SweepInterval"] = SweepInterval.ToString()
            });
        });
    }
}
