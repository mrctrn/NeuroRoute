using System.Text.Json.Serialization;

namespace NeuroRoute.Dashboard.Models;

public sealed class MetricsSnapshot
{
    [JsonPropertyName("totalRequests")]
    public long TotalRequests { get; set; }

    [JsonPropertyName("npuHandled")]
    public long NpuHandled { get; set; }

    [JsonPropertyName("gpuEscalated")]
    public long GpuEscalated { get; set; }

    [JsonPropertyName("streamingRequests")]
    public long StreamingRequests { get; set; }

    [JsonPropertyName("byTaskType")]
    public Dictionary<string, long> ByTaskType { get; set; } = [];

    [JsonPropertyName("byCase")]
    public Dictionary<string, long> ByCase { get; set; } = [];

    [JsonPropertyName("durationMs")]
    public DurationStats DurationMs { get; set; } = new();
}

public sealed class DurationStats
{
    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }
}
