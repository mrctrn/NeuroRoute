using System.Text.Json.Serialization;

namespace NeuroRoute.Dashboard.Models;

public sealed class HealthStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = "";

    [JsonPropertyName("components")]
    public Dictionary<string, ComponentHealth> Components { get; set; } = [];
}

public sealed class ComponentHealth
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
