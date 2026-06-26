using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed class NpuPlan
{
    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = "simple_chat";

    [JsonPropertyName("needs_gpu")]
    public bool NeedsGpu { get; set; }

    [JsonPropertyName("compressed_prompt")]
    public string CompressedPrompt { get; set; } = string.Empty;

    [JsonPropertyName("notes_for_gpu")]
    public string NotesForGpu { get; set; } = string.Empty;

    public int EstimatedTokens { get; set; }
    public float Confidence { get; set; }

    [JsonPropertyName("routing_case")]
    public string RoutingCase { get; set; } = "A";
}
