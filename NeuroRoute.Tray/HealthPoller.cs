namespace NeuroRoute.Tray;

public sealed class HealthPoller
{
    private readonly ServiceClient _client;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;

    public event EventHandler<HealthResult>? OnHealthUpdated;

    public HealthPoller(ServiceClient client, TimeSpan interval)
    {
        _client = client;
        _interval = interval;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = PollLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _client.GetAsync("health");
                var health = HealthResult.FromJson(json);
                OnHealthUpdated?.Invoke(this, health);
            }
            catch
            {
                OnHealthUpdated?.Invoke(this, new HealthResult
                {
                    Status = "Unreachable", NpuStatus = "?", GpuStatus = "?"
                });
            }

            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
