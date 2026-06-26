namespace NeuroRoute.Service.Models;

public sealed class AdminLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}
