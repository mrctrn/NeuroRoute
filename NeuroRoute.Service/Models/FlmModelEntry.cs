using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed record FlmModelEntry(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("installed")] bool Installed,
    [property: JsonPropertyName("size")] string? Size,
    [property: JsonPropertyName("quantization")] string? Quantization,
    [property: JsonPropertyName("description")] string? Description
);
