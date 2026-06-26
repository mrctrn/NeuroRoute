using System.Net.Http.Json;
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
}
