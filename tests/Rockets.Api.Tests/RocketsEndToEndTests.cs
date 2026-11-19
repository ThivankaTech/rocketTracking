using System.Net;
using System.Net.Http.Json;

namespace Rockets.Api.Tests;

public sealed class RocketsEndToEndTests : IClassFixture<RocketsApiFactory>
{
    private readonly RocketsApiFactory _factory;
    private readonly HttpClient _client;

    public RocketsEndToEndTests(RocketsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewChannel() => $"e2e-{Guid.NewGuid():N}";

    [Fact]
    public async Task Launch_CreatesRocket_WithLaunchSpeedAsInitialSpeed()
    {
        var channel = NewChannel();

        var response = await RocketMessages.PostAsync(
            _client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 1);

        Assert.NotNull(rocket);
        Assert.Equal(channel, rocket!.Channel);
        Assert.Equal("InFlight", rocket.Status);
        Assert.Equal("Falcon-9", rocket.Type);
        Assert.Equal("ARTEMIS", rocket.Mission);
        Assert.Equal(500, rocket.LaunchSpeed);
        Assert.Equal(500, rocket.Speed);
        Assert.Null(rocket.ExplosionReason);
        Assert.False(rocket.HasSkippedMessages);
    }

    [Fact]
    public async Task SpeedIncreasedAndDecreased_AdjustSpeedAdditively()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 3000 });
        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedDecreased", new { by = 1000 });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 3);

        Assert.NotNull(rocket);
        Assert.Equal(500 + 3000 - 1000, rocket!.Speed);
    }

    [Fact]
    public async Task MissionChanged_UpdatesMission()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketMissionChanged",
            new { newMission = "SHUTTLE_MIR" });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 2);

        Assert.NotNull(rocket);
        Assert.Equal("SHUTTLE_MIR", rocket!.Mission);
    }

    [Fact]
    public async Task Exploded_SetsStatusAndFreezesFurtherSpeedChanges()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketExploded",
            new { reason = "PRESSURE_VESSEL_FAILURE" });
        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 9999 });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 3);

        Assert.NotNull(rocket);
        Assert.Equal("Exploded", rocket!.Status);
        Assert.Equal("PRESSURE_VESSEL_FAILURE", rocket.ExplosionReason);
        Assert.Equal(500, rocket.Speed);
    }

    [Fact]
    public async Task OutOfOrderMessages_AreBufferedThenAppliedInOrder()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketMissionChanged",
            new { newMission = "SHUTTLE_MIR" });
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 3000 });

        await Task.Delay(500);
        var prematureResponse = await _client.GetAsync($"/rockets/{channel}");
        Assert.Equal(HttpStatusCode.NotFound, prematureResponse.StatusCode);

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 3);

        Assert.NotNull(rocket);
        Assert.Equal(3500, rocket!.Speed);
        Assert.Equal("SHUTTLE_MIR", rocket.Mission);
    }

    [Fact]
    public async Task DuplicateMessage_IsAppliedOnlyOnce()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 3000 });

        var first = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 2);
        Assert.NotNull(first);
        Assert.Equal(3500, first!.Speed);

        for (var i = 0; i < 3; i++)
        {
            await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 3000 });
        }

        await Task.Delay(1500);

        var afterRedelivery = await _client.GetFromJsonAsync<RocketApiResponse>($"/rockets/{channel}");
        Assert.NotNull(afterRedelivery);
        Assert.Equal(3500, afterRedelivery!.Speed);
        Assert.Equal(2, afterRedelivery.LastMessageNumber);
    }

    [Fact]
    public async Task GapExceedingTimeout_SkipsAheadAndRecordsSkippedCount()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 1);

        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 1000 });

        await Task.Delay(_factory.GapTimeout + TimeSpan.FromSeconds(1));

        await RocketMessages.PostAsync(_client, channel, 4, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 7 });
        await RocketMessages.PostAsync(_client, channel, 5, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 3 });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 5);

        Assert.NotNull(rocket);
        Assert.True(rocket!.HasSkippedMessages);
        Assert.Equal(1, rocket.SkippedMessageCount);
        Assert.Equal(500 + 1000 + 7 + 3, rocket.Speed);
    }

    [Fact]
    public async Task StuckChannelWithNoFurtherMessages_IsResolvedByPeriodicSweepAlone()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 1);

        // #2 never arrives, and we never post anything else on this channel again.
        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 1000 });

        var stillStuck = await _client.GetFromJsonAsync<RocketApiResponse>($"/rockets/{channel}");
        Assert.Equal(1, stillStuck!.LastMessageNumber);

        // Only the passage of time + the periodic sweep can resolve this now - no further POST happens.
        var rocket = await RocketMessages.WaitForStateAsync(
            _client, channel, r => r.LastMessageNumber == 3,
            _factory.GapTimeout + _factory.SweepInterval + TimeSpan.FromSeconds(5));

        Assert.NotNull(rocket);
        Assert.True(rocket!.HasSkippedMessages);
        Assert.Equal(1, rocket.SkippedMessageCount);
        Assert.Equal(500 + 1000, rocket.Speed);
    }

    [Fact]
    public async Task LargeStaleBacklog_ResolvedBySweep_WhileNewOutOfOrderMessagesArriveConcurrently()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });
        await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 1);

        // #2 never arrives. Buffer a large backlog (8 messages) behind that one missing number.
        for (var messageNumber = 3; messageNumber <= 10; messageNumber++)
        {
            await RocketMessages.PostAsync(
                _client, channel, messageNumber, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });
        }

        // Let the backlog cross the staleness threshold - this is the window where the sweep
        // becomes eligible to act on this channel (and therefore holds the Redis lock on it).
        await Task.Delay(_factory.GapTimeout + TimeSpan.FromSeconds(1));

        // Fire new messages for the SAME channel, deliberately out of order, right as the sweep
        // is likely mid-resolution - this exercises the lock serializing sweep vs. reactive processing.
        await RocketMessages.PostAsync(_client, channel, 12, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });
        await RocketMessages.PostAsync(_client, channel, 13, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });
        await RocketMessages.PostAsync(_client, channel, 11, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });

        var rocket = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 13);

        Assert.NotNull(rocket);
        Assert.True(rocket!.HasSkippedMessages);
        Assert.Equal(1, rocket.SkippedMessageCount);
        Assert.Equal(500 + (8 * 100) + (3 * 100), rocket.Speed);
    }

    [Fact]
    public async Task MissingLaunchMessage_StillTracksRocketWithUnknownLaunchDetails()
    {
        var channel = NewChannel();

        // The launch message (#1) is never sent - only speed changes arrive.
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });
        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 200 });

        var rocket = await RocketMessages.WaitForStateAsync(
            _client, channel, r => r.LastMessageNumber == 3,
            _factory.GapTimeout + _factory.SweepInterval + TimeSpan.FromSeconds(5));

        Assert.NotNull(rocket);
        Assert.False(rocket!.HasLaunchDetails);
        Assert.Null(rocket.Type);
        Assert.Null(rocket.Mission);
        Assert.Null(rocket.LaunchSpeed);
        Assert.Equal(300, rocket.Speed);
        Assert.Equal(1, rocket.SkippedMessageCount);
    }

    [Fact]
    public async Task ExplodedBeforeLaunchMessageArrives_IsStillRecordedAsExploded()
    {
        var channel = NewChannel();

        // The rocket explodes, but its launch message never arrives.
        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketExploded",
            new { reason = "PRESSURE_VESSEL_FAILURE" });

        var rocket = await RocketMessages.WaitForStateAsync(
            _client, channel, r => r.LastMessageNumber == 2,
            _factory.GapTimeout + _factory.SweepInterval + TimeSpan.FromSeconds(5));

        Assert.NotNull(rocket);
        Assert.Equal("Exploded", rocket!.Status);
        Assert.Equal("PRESSURE_VESSEL_FAILURE", rocket.ExplosionReason);
        Assert.False(rocket.HasLaunchDetails);
        Assert.Null(rocket.Type);
    }

    [Fact]
    public async Task LaunchMessageArrivingAfterGracePeriod_IsDroppedLikeAnyOtherLateMessage()
    {
        var channel = NewChannel();

        await RocketMessages.PostAsync(_client, channel, 2, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 100 });
        await RocketMessages.PostAsync(_client, channel, 3, DateTimeOffset.UtcNow, "RocketSpeedIncreased", new { by = 200 });

        var partial = await RocketMessages.WaitForStateAsync(
            _client, channel, r => r.LastMessageNumber == 3,
            _factory.GapTimeout + _factory.SweepInterval + TimeSpan.FromSeconds(5));

        Assert.NotNull(partial);
        Assert.False(partial!.HasLaunchDetails);
        Assert.Equal(1, partial.SkippedMessageCount);

        // The launch message finally turns up, long after we gave up waiting for it - it should
        // be ignored just like any other message arriving after the grace period.
        await RocketMessages.PostAsync(_client, channel, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 500, mission = "ARTEMIS" });

        var afterLateArrival = await RocketMessages.WaitForStateAsync(_client, channel, r => r.LastMessageNumber == 3);

        Assert.NotNull(afterLateArrival);
        Assert.False(afterLateArrival!.HasLaunchDetails);
        Assert.Null(afterLateArrival.Type);
        Assert.Equal(300, afterLateArrival.Speed);
        Assert.Equal(1, afterLateArrival.SkippedMessageCount);
    }

    [Fact]
    public async Task GetAll_ReturnsRocketsSortedBySpeedDescending()
    {
        var slow = NewChannel();
        var fast = NewChannel();

        await RocketMessages.PostAsync(_client, slow, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 100, mission = "ARTEMIS" });
        await RocketMessages.PostAsync(_client, fast, 1, DateTimeOffset.UtcNow, "RocketLaunched",
            new { type = "Falcon-9", launchSpeed = 9000, mission = "ARTEMIS" });

        await RocketMessages.WaitForStateAsync(_client, slow, r => r.LastMessageNumber == 1);
        await RocketMessages.WaitForStateAsync(_client, fast, r => r.LastMessageNumber == 1);

        var rockets = await _client.GetFromJsonAsync<List<RocketApiResponse>>("/rockets");

        Assert.NotNull(rockets);

        var slowIndex = rockets!.FindIndex(r => r.Channel == slow);
        var fastIndex = rockets.FindIndex(r => r.Channel == fast);

        Assert.True(slowIndex >= 0 && fastIndex >= 0);
        Assert.True(fastIndex < slowIndex);
    }

    [Fact]
    public async Task UnknownChannel_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/rockets/{NewChannel()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownMessageType_ReturnsBadRequest()
    {
        var response = await RocketMessages.PostAsync(
            _client, NewChannel(), 1, DateTimeOffset.UtcNow, "NotARealMessageType", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
