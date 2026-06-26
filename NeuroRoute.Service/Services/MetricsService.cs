using System.Collections.Concurrent;

namespace NeuroRoute.Service.Services;

public sealed class MetricsService
{
    private long _totalRequests;
    private long _npuHandled;
    private long _gpuEscalated;
    private long _streamingRequests;

    private readonly ConcurrentDictionary<string, long> _routingByTaskType = new();
    private readonly ConcurrentDictionary<string, long> _routingByCase = new();

    private readonly ConcurrentQueue<double> _recentDurations = new();
    private const int MaxRecentDurations = 1000;

    public void RecordRoutingDecision(string taskType, string routingCase, bool needsGpu, bool isStreaming, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);

        if (needsGpu)
            Interlocked.Increment(ref _gpuEscalated);
        else
            Interlocked.Increment(ref _npuHandled);

        if (isStreaming)
            Interlocked.Increment(ref _streamingRequests);

        _routingByTaskType.AddOrUpdate(taskType, 1, (_, c) => c + 1);

        var caseKey = $"case_{routingCase}";
        _routingByCase.AddOrUpdate(caseKey, 1, (_, c) => c + 1);

        _recentDurations.Enqueue(durationMs);
        while (_recentDurations.Count > MaxRecentDurations)
            _recentDurations.TryDequeue(out _);
    }

    public MetricsSnapshot GetSnapshot()
    {
        var durations = _recentDurations.ToArray();
        var sorted = durations.OrderBy(d => d).ToArray();

        return new MetricsSnapshot
        {
            TotalRequests = Interlocked.Read(ref _totalRequests),
            NpuHandled = Interlocked.Read(ref _npuHandled),
            GpuEscalated = Interlocked.Read(ref _gpuEscalated),
            StreamingRequests = Interlocked.Read(ref _streamingRequests),
            ByTaskType = _routingByTaskType.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            ByCase = _routingByCase.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            DurationMs = new DurationStats
            {
                Min = durations.Length > 0 ? sorted.First() : 0,
                Max = durations.Length > 0 ? sorted.Last() : 0,
            }
        };
    }
}

public sealed class MetricsSnapshot
{
    public long TotalRequests { get; set; }
    public long NpuHandled { get; set; }
    public long GpuEscalated { get; set; }
    public long StreamingRequests { get; set; }
    public Dictionary<string, long> ByTaskType { get; set; } = [];
    public Dictionary<string, long> ByCase { get; set; } = [];
    public DurationStats DurationMs { get; set; } = new();
}

public sealed class DurationStats
{
    public double Min { get; set; }
    public double Max { get; set; }
}
