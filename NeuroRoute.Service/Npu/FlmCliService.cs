using System.Diagnostics;
using System.Text.Json;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Npu;

public sealed class FlmCliService
{
    private readonly string _flmPath;
    private readonly ILogger<FlmCliService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FlmCliService(string flmPath, ILogger<FlmCliService> logger)
    {
        _flmPath = flmPath;
        _logger = logger;
    }

    public async Task<List<FlmModelEntry>> ListModelsAsync(string filter = "installed", CancellationToken ct = default)
    {
        var validFilters = new[] { "all", "installed", "not-installed" };
        if (!validFilters.Contains(filter))
            filter = "installed";

        var (exitCode, stdout) = await RunFlmAsync($"list --json --filter {filter}", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];

        try
        {
            var models = JsonSerializer.Deserialize<List<FlmModelEntry>>(stdout, JsonOptions);
            return models ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse flm list output");
            return [];
        }
    }

    public async Task<bool> PullModelAsync(string tag, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var (exitCode, _) = await RunFlmAsync($"pull {tag}", ct, onOutput);
        return exitCode == 0;
    }

    public async Task<bool> RemoveModelAsync(string tag, CancellationToken ct = default)
    {
        var (exitCode, _) = await RunFlmAsync($"remove {tag}", ct);
        return exitCode == 0;
    }

    private async Task<(int ExitCode, string Stdout)> RunFlmAsync(
        string arguments,
        CancellationToken ct,
        Action<string>? onOutput = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _flmPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
            onOutput?.Invoke($"[stderr] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, outputBuilder.ToString());
    }
}
