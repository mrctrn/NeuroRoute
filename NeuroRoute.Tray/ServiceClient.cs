namespace NeuroRoute.Tray;

public sealed class ServiceClient
{
    private readonly HttpClient _http;
    private readonly string _adminKey;

    public ServiceClient(string baseUrl, string adminKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/v1/") };
        _adminKey = adminKey;
    }

    public async Task<string?> GetAsync(string path)
    {
        var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task PostAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (!string.IsNullOrEmpty(_adminKey))
            request.Headers.Add("X-NeuroRoute-Admin-Key", _adminKey);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
