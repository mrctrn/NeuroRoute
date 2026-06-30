using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed class NeuroRouteMetadata
{
    [JsonPropertyName("backend")]
    public string Backend { get; init; } = "npu";

    [JsonPropertyName("fallback")]
    public bool Fallback { get; init; }

    [JsonPropertyName("passthrough")]
    public bool Passthrough { get; init; }

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; init; }

    [JsonPropertyName("task_type")]
    public string? TaskType { get; init; }

    [JsonPropertyName("routing_case")]
    public string? RoutingCase { get; init; }
}
