using System.Diagnostics;
using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private Process? _serviceProcess;
    private Process? _dashboardProcess;
    private const string ServiceUrl = "http://localhost:5000";
    private const string DashboardUrl = "http://localhost:5001";

    public async Task InitializeAsync()
    {
        await KillProcessOnPort(5000);
        await KillProcessOnPort(5001);

        var solutionRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var serviceRoot = Path.Combine(solutionRoot, "NeuroRoute.Service");
        _serviceProcess = StartDotNetRun(serviceRoot,
            ("NeuroRoute__UseMockBackends", "true"),
            ("Kestrel__Endpoints__Http__Url", ServiceUrl));
        await WaitForHealth(ServiceUrl);

        var dashboardRoot = Path.Combine(solutionRoot, "NeuroRoute.Dashboard");
        _dashboardProcess = StartDotNetRun(dashboardRoot,
            ("Kestrel__Endpoints__Http__Url", DashboardUrl));
        await WaitForHttp(DashboardUrl);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProcess is not null && !_serviceProcess.HasExited)
            _serviceProcess.Kill();
        if (_dashboardProcess is not null && !_dashboardProcess.HasExited)
            _dashboardProcess.Kill();

        await KillProcessOnPort(5000);
        await KillProcessOnPort(5001);
    }

    public async Task ProgramScenarioAsync(object body)
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/admin/mock/scenario", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetScenarioAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var response = await client.PostAsync("/v1/admin/mock/scenario/reset", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task MakeChatRequestAsync(object requestBody)
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task NavigateToDashboardAsync(IPage page)
    {
        await page.GotoAsync(DashboardUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Process StartDotNetRun(string projectDir, params (string key, string value)[] envVars)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in envVars)
            psi.EnvironmentVariables[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static async Task WaitForHealth(string baseUrl, int timeoutMs = 30_000)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var response = await client.GetAsync($"{baseUrl}/v1/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Service at {baseUrl} did not become healthy within {timeoutMs}ms");
    }

    private static async Task WaitForHttp(string url, int timeoutMs = 30_000)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"HTTP endpoint at {url} did not respond within {timeoutMs}ms");
    }

    private static async Task KillProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return;

            var output = await proc.StandardOutput.ReadToEndAsync();
            foreach (var line in output.Split(Environment.NewLine))
            {
                if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid))
                    {
                        try { Process.GetProcessById(pid).Kill(); }
                        catch { }
                    }
                }
            }
        }
        catch
        {
        }
    }
}
