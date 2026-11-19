using System.Text.Json;
using Rockets.Domain;

namespace Rockets.Infrastructure;

public static class RocketEventPayloadSerializer
{
    public static (string PayloadType, string PayloadJson) Serialize(RocketEventPayload payload) => payload switch
    {
        RocketLaunched p => (nameof(RocketLaunched), JsonSerializer.Serialize(p)),
        RocketSpeedIncreased p => (nameof(RocketSpeedIncreased), JsonSerializer.Serialize(p)),
        RocketSpeedDecreased p => (nameof(RocketSpeedDecreased), JsonSerializer.Serialize(p)),
        RocketMissionChanged p => (nameof(RocketMissionChanged), JsonSerializer.Serialize(p)),
        RocketExploded p => (nameof(RocketExploded), JsonSerializer.Serialize(p)),
        _ => throw new NotSupportedException($"Unhandled payload type {payload.GetType().Name}.")
    };

    public static RocketEventPayload Deserialize(string payloadType, string payloadJson) => payloadType switch
    {
        nameof(RocketLaunched) => JsonSerializer.Deserialize<RocketLaunched>(payloadJson)!,
        nameof(RocketSpeedIncreased) => JsonSerializer.Deserialize<RocketSpeedIncreased>(payloadJson)!,
        nameof(RocketSpeedDecreased) => JsonSerializer.Deserialize<RocketSpeedDecreased>(payloadJson)!,
        nameof(RocketMissionChanged) => JsonSerializer.Deserialize<RocketMissionChanged>(payloadJson)!,
        nameof(RocketExploded) => JsonSerializer.Deserialize<RocketExploded>(payloadJson)!,
        _ => throw new NotSupportedException($"Unhandled payload type {payloadType}.")
    };
}
