using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed class HealthStatus
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "healthy";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.0.0.0";

    [JsonPropertyName("uptime")]
    public string Uptime { get; init; } = "00:00:00";

    [JsonPropertyName("components")]
    public Dictionary<string, ComponentHealth> Components { get; init; } = [];
}

public sealed class ComponentHealth
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "unknown";

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
