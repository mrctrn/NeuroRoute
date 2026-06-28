using System.Text.Json.Serialization;

namespace NeuroRoute.Dashboard.Models;

public sealed class NpuStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("modelTag")]
    public string ModelTag { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("ctxLen")]
    public int CtxLen { get; set; }

    [JsonPropertyName("pmode")]
    public string Pmode { get; set; } = "";

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }
}

public sealed record FlmModelEntry(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("installed")] bool Installed,
    [property: JsonPropertyName("size")] string? Size,
    [property: JsonPropertyName("quantization")] string? Quantization,
    [property: JsonPropertyName("description")] string? Description
);
