using System.Text.Json.Serialization;

namespace NeuroRoute.Dashboard.Models;

public sealed class AdminLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
