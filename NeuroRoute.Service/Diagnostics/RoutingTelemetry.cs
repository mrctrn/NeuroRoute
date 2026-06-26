using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NeuroRoute.Service.Diagnostics;

public static class RoutingTelemetry
{
    public static readonly ActivitySource ActivitySource = new("NeuroRoute");
    public static readonly Meter Meter = new("NeuroRoute");

    // Routing counters
    public static readonly Counter<long> RoutingDecisions = Meter.CreateCounter<long>(
        "neuroroute.routing.decisions",
        description: "Number of routing decisions by type");

    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "neuroroute.request.duration",
        unit: "ms",
        description: "Request processing duration in milliseconds");

    public static readonly Histogram<long> EstimatedTokens = Meter.CreateHistogram<long>(
        "neuroroute.tokens.estimated",
        unit: "tokens",
        description: "Estimated token counts per request");

    public static readonly Counter<long> GpuRequests = Meter.CreateCounter<long>(
        "neuroroute.gpu.requests",
        description: "Number of GPU escalation requests");

    public static readonly Counter<long> NpuRequests = Meter.CreateCounter<long>(
        "neuroroute.npu.requests",
        description: "Number of NPU-handled requests");

    public static void RecordRoutingDecision(string taskType, bool needsGpu, int estimatedTokens, string backend)
    {
        var tags = new TagList
        {
            { "task_type", taskType },
            { "needs_gpu", needsGpu },
            { "backend", backend }
        };
        RoutingDecisions.Add(1, tags);
        EstimatedTokens.Record(estimatedTokens, tags);

        if (needsGpu)
            GpuRequests.Add(1);
        else
            NpuRequests.Add(1);
    }
}
