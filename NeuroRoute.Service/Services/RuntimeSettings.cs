namespace NeuroRoute.Service.Services;

public sealed class RuntimeSettings
{
    public bool PassthroughMode { get; set; }
    public bool GpuFallbackToNpu { get; set; } = true;
}
