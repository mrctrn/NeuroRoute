using System.Net.Http.Json;
using System.Text.Json;
using NeuroRoute.Dashboard.Models;

namespace NeuroRoute.Dashboard.Services;

public sealed class NeuroRouteApiClient
{
    private readonly HttpClient _http;

    public NeuroRouteApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<HealthStatus?> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HealthStatus>("/v1/health", ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<MetricsSnapshot?> GetMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MetricsSnapshot>("/v1/metrics", ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> RestartBackendAsync()
    {
        try
        {
            var response = await _http.PostAsync("/v1/admin/restart-backend", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ReloadConfigAsync()
    {
        try
        {
            var response = await _http.PostAsync("/v1/admin/reload-config", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<AdminLogEntry>?> GetLogsAsync(int count = 50, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AdminLogEntry>>($"/v1/admin/logs?count={count}", ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> StopServiceAsync()
    {
        try
        {
            var response = await _http.PostAsync("/v1/admin/stop", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<NpuStatus?> GetNpuStatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<NpuStatus>("/v1/admin/npu", ct);
        }
        catch { return null; }
    }

    public async Task<List<FlmModelEntry>?> GetNpuModelsAsync(string filter = "installed", CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<FlmModelEntry>>($"/v1/admin/npu/models?filter={filter}", ct);
        }
        catch { return null; }
    }

    public async Task<bool> LoadNpuModelAsync(string modelTag, int ctxLen, string pmode, bool persist)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/v1/admin/npu/load", new
            {
                modelTag, ctxLen, pmode, persist
            });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> PullNpuModelAsync(string tag)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/v1/admin/npu/pull", new { tag });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RemoveNpuModelAsync(string tag)
    {
        try
        {
            var response = await _http.DeleteAsync($"/v1/admin/npu/models/{tag}");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
