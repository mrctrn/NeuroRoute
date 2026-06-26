using System.Text.Json;

namespace NeuroRoute.Tray;

public class HealthResult
{
    public string Status { get; set; } = "Unknown";
    public string NpuStatus { get; set; } = "Unknown";
    public string GpuStatus { get; set; } = "Unknown";
    public string NpuModel { get; set; } = "";
    public string GpuModel { get; set; } = "";

    public string IconState => Status.ToLowerInvariant() switch
    {
        "healthy" => "green",
        "degraded" => "yellow",
        "unhealthy" => "red",
        _ => "gray"
    };

    public string StatusIcon => Status.ToLowerInvariant() switch
    {
        "healthy" => "\u25CF",
        "degraded" => "\u25CF",
        "unhealthy" => "\u25CF",
        _ => "\u25CB"
    };

    public string NpuIcon => NpuStatus.ToLowerInvariant() switch
    {
        "healthy" => "\u25CF",
        "degraded" => "\u25CF",
        "unhealthy" => "\u25CF",
        _ => "\u25CB"
    };

    public string GpuIcon => GpuStatus.ToLowerInvariant() switch
    {
        "healthy" => "\u25CF",
        "degraded" => "\u25CF",
        "unhealthy" => "\u25CF",
        _ => "\u25CB"
    };

    public static HealthResult FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new HealthResult { Status = "Unreachable", NpuStatus = "?", GpuStatus = "?" };

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new HealthResult
            {
                Status = root.GetProperty("status").GetString() ?? "Unknown"
            };

            if (root.TryGetProperty("components", out var comps))
            {
                if (comps.TryGetProperty("npu", out var npu))
                {
                    result.NpuStatus = npu.GetProperty("status").GetString() ?? "?";
                    if (npu.TryGetProperty("model", out var m)) result.NpuModel = m.GetString() ?? "";
                }
                if (comps.TryGetProperty("gpu", out var gpu))
                {
                    result.GpuStatus = gpu.GetProperty("status").GetString() ?? "?";
                    if (gpu.TryGetProperty("model", out var m)) result.GpuModel = m.GetString() ?? "";
                    if (!string.IsNullOrEmpty(result.GpuModel))
                    {
                        var shortName = Path.GetFileName(result.GpuModel);
                        result.GpuStatus += $" ({shortName})";
                    }
                }
            }

            return result;
        }
        catch
        {
            return new HealthResult { Status = "Error", NpuStatus = "?", GpuStatus = "?" };
        }
    }
}
