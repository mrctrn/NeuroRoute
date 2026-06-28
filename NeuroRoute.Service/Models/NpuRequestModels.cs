using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed record NpuLoadRequest(
    [property: JsonPropertyName("modelTag")] string ModelTag,
    [property: JsonPropertyName("ctxLen")] int CtxLen = 0,
    [property: JsonPropertyName("pmode")] string Pmode = "performance",
    [property: JsonPropertyName("persist")] bool Persist = false
);

public sealed record NpuPullRequest(
    [property: JsonPropertyName("tag")] string Tag
);
