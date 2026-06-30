using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed class ChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<ChunkChoice> Choices { get; set; } = [];

    [JsonPropertyName("_neuroroute")]
    public NeuroRouteMetadata? NeuroRouteMeta { get; set; }
}

public sealed class ChunkChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public ChatMessage Delta { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}
