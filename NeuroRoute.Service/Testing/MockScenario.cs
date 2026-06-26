namespace NeuroRoute.Service.Testing;

public sealed class MockScenario
{
    public bool NpuAvailable { get; set; } = true;
    public string NpuBackend { get; set; } = "mock";
    public string NpuModel { get; set; } = "mock-npu-model-v1";

    public string TaskType { get; set; } = "simple_chat";
    public bool NeedsGpu { get; set; } = false;
    public string RoutingCase { get; set; } = "C";

    public string NpuResponseText { get; set; } = "Hello from mock NPU!";

    public string GpuResponseText { get; set; } = "Complex reasoning from mock GPU!";
    public bool GpuAvailable { get; set; } = true;
    public string GpuModel { get; set; } = "mock-gpu-model-v1";
    public string GpuEndpoint { get; set; } = "http://mock-gpu:8080";

    public int SimulatedLatencyMs { get; set; } = 50;
    public int StreamDelayMs { get; set; } = 10;

    public void ResetToDefaults()
    {
        NpuAvailable = true;
        NpuBackend = "mock";
        NpuModel = "mock-npu-model-v1";
        TaskType = "simple_chat";
        NeedsGpu = false;
        RoutingCase = "C";
        NpuResponseText = "Hello from mock NPU!";
        GpuResponseText = "Complex reasoning from mock GPU!";
        GpuAvailable = true;
        GpuModel = "mock-gpu-model-v1";
        GpuEndpoint = "http://mock-gpu:8080";
        SimulatedLatencyMs = 50;
        StreamDelayMs = 10;
    }
}
