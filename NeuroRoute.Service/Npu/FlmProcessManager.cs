using System.Diagnostics;

namespace NeuroRoute.Service.Npu;

public sealed class FlmProcessManager : IDisposable
{
    private readonly string _modelTag;
    private readonly FlmClient _flmClient;
    private readonly ILogger<FlmProcessManager> _logger;
    private Process? _process;
    private int _restartCount;
    private const int MaxRestarts = 3;
    private const int HealthPollIntervalMs = 500;
    private const int HealthPollTimeoutMs = 30_000;
    private const int ShutdownGracePeriodMs = 5_000;
    private bool _disposed;

    public string Status { get; private set; } = "unavailable";
    public string? StatusMessage { get; private set; }

    public FlmProcessManager(string modelTag, FlmClient flmClient, ILogger<FlmProcessManager> logger)
    {
        _modelTag = modelTag;
        _flmClient = flmClient;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FlmProcessManager));

        try
        {
            var flmPath = FindFlmExecutable();
            if (flmPath is null)
            {
                Status = "unhealthy";
                StatusMessage = "flm.exe not found in PATH";
                _logger.LogError("FLM executable not found. Install FastFlowLM or add it to PATH.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = flmPath,
                Arguments = $"serve {_modelTag}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    _logger.LogDebug("FLM: {Output}", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    _logger.LogWarning("FLM: {Error}", e.Data);
            };
            _process.Exited += OnProcessExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logger.LogInformation("FLM process started (PID: {Pid})", _process.Id);

            var ready = await WaitForReadyAsync(ct);
            if (ready)
            {
                Status = "healthy";
                StatusMessage = $"FLM server ready (PID: {_process.Id})";
                _logger.LogInformation("FLM backend ready");
            }
            else
            {
                Status = "unhealthy";
                StatusMessage = "FLM server failed to respond to health checks";
                _logger.LogWarning("FLM server did not become ready within timeout");
            }
        }
        catch (Exception ex)
        {
            Status = "unhealthy";
            StatusMessage = ex.Message;
            _logger.LogError(ex, "Failed to start FLM process");
        }
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
            return;

        _logger.LogInformation("Stopping FLM process (PID: {Pid})...", _process.Id);

        // Try graceful shutdown via CtrlBreak
        if (_process.CloseMainWindow())
        {
            var exited = _process.WaitForExit(ShutdownGracePeriodMs);
            if (exited) return;
        }

        // Force kill
        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        _logger.LogInformation("FLM process stopped");
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        if (_restartCount >= MaxRestarts)
        {
            _logger.LogError("FLM restart limit reached ({MaxRestarts}), not restarting", MaxRestarts);
            Status = "unhealthy";
            StatusMessage = "FLM process restart limit exceeded";
            return;
        }

        _restartCount++;
        _logger.LogInformation("Restarting FLM process (attempt {Attempt}/{MaxRestarts})",
            _restartCount, MaxRestarts);

        await StopAsync();
        _process?.Dispose();
        _process = null;

        await Task.Delay(1_000, ct);
        await StartAsync(ct);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        _logger.LogWarning("FLM process exited unexpectedly (exit code: {ExitCode})",
            _process?.ExitCode);

        Status = "unhealthy";
        StatusMessage = $"FLM process exited (code: {_process?.ExitCode})";

        // Trigger restart in background
        _ = RestartAsync();
    }

    private async Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HealthPollTimeoutMs);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (await _flmClient.PingAsync(cts.Token))
                    return true;

                await Task.Delay(HealthPollIntervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return false;
    }

    private static string? FindFlmExecutable()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var path in paths)
        {
            var full = Path.Combine(path.Trim(), "flm.exe");
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process is not null)
        {
            _process.Exited -= OnProcessExited;
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3_000);
            }
            _process.Dispose();
        }
    }
}
