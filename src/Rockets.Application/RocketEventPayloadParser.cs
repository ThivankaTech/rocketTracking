using System.Text.Json;
using Rockets.Domain;

namespace Rockets.Application;

public static class RocketEventPayloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Result<RocketEventPayload> Parse(string messageType, string messageJson)
    {
        try
        {
            RocketEventPayload? payload = messageType switch
            {
                nameof(RocketLaunched) => JsonSerializer.Deserialize<RocketLaunched>(messageJson, JsonOptions),
                nameof(RocketSpeedIncreased) => JsonSerializer.Deserialize<RocketSpeedIncreased>(messageJson, JsonOptions),
                nameof(RocketSpeedDecreased) => JsonSerializer.Deserialize<RocketSpeedDecreased>(messageJson, JsonOptions),
                nameof(RocketMissionChanged) => JsonSerializer.Deserialize<RocketMissionChanged>(messageJson, JsonOptions),
                nameof(RocketExploded) => JsonSerializer.Deserialize<RocketExploded>(messageJson, JsonOptions),
                _ => null
            };

            return payload is null
                ? Result<RocketEventPayload>.Failure($"Unknown messageType '{messageType}'.")
                : Result<RocketEventPayload>.Success(payload);
        }
        catch (JsonException ex)
        {
            return Result<RocketEventPayload>.Failure($"Malformed message body for '{messageType}': {ex.Message}");
        }
    }
}
