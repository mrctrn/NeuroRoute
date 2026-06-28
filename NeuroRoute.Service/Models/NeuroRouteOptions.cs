namespace NeuroRoute.Service.Models;

public sealed class NeuroRouteOptions
{
    public const string SectionName = "NeuroRoute";

    public string NpuBackend { get; set; } = "onnx";
    public string NpuModelPath { get; set; } = "Models/gemma-4-int4.onnx";
    public string NpuFlmModelTag { get; set; } = "gemma4-it:e4b";
    public string NpuFlmEndpoint { get; set; } = "http://127.0.0.1:52625";
    public string NpuFlmHost { get; set; } = "0.0.0.0";
    public int NpuFlmPort { get; set; } = 52625;
    public int NpuFlmCtxLen { get; set; }
    public string NpuFlmPmode { get; set; } = "performance";
    public string GpuEndpoint { get; set; } = "http://localhost:8080";
    public int NpuLimit { get; set; } = 65536;
    public int NpuSlice { get; set; } = 2048;
    public int GpuMaxRetries { get; set; } = 3;
    public int GpuTimeoutSeconds { get; set; } = 300;
    public bool UseMockBackends { get; set; }
}
