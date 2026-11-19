using System.Text.Json;

namespace Rockets.Api;

public sealed record RocketMessageEnvelope(RocketMessageMetadata Metadata, JsonElement Message);

public sealed record RocketMessageMetadata(string Channel, int MessageNumber, DateTimeOffset MessageTime, string MessageType);
