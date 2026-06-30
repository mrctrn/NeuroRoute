using System.Text.Json.Serialization;

namespace NeuroRoute.Dashboard.Models;

public sealed class RuntimeSettingsDto
{
    [JsonPropertyName("passthroughMode")] public bool PassthroughMode { get; set; }
    [JsonPropertyName("gpuFallbackToNpu")] public bool GpuFallbackToNpu { get; set; }
}
