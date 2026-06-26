namespace NeuroRoute.Service.Testing;

public sealed class MockScenarioRequest
{
    public bool? NpuAvailable { get; init; }
    public string? NpuBackend { get; init; }
    public string? NpuModel { get; init; }
    public string? TaskType { get; init; }
    public bool? NeedsGpu { get; init; }
    public string? RoutingCase { get; init; }
    public string? NpuResponseText { get; init; }
    public string? GpuResponseText { get; init; }
    public bool? GpuAvailable { get; init; }
    public string? GpuModel { get; init; }
    public string? GpuEndpoint { get; init; }
    public int? SimulatedLatencyMs { get; init; }
    public int? StreamDelayMs { get; init; }
}
