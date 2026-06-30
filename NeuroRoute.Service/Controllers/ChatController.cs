using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Routing;
using NeuroRoute.Service.Services;
using Microsoft.AspNetCore.Http;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly Router _router;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(Router router, RuntimeSettings runtimeSettings, ILogger<ChatController> logger)
    {
        _router = router;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    [HttpPost("completions")]
    public async Task CreateCompletion([FromBody] ChatRequest request, CancellationToken ct)
    {
        Response.Headers["X-NeuroRoute-Mode"] = _runtimeSettings.PassthroughMode ? "passthrough" : "routing";

        if (request.Stream)
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            try
            {
                var first = true;
                await foreach (var chunk in _router.StreamAsync(request, ct))
                {
                    if (first)
                    {
                        first = false;
                        if (chunk.NeuroRouteMeta is not null)
                        {
                            Response.Headers["X-NeuroRoute-Backend"] = chunk.NeuroRouteMeta.Backend;
                            Response.Headers["X-NeuroRoute-Fallback"] = chunk.NeuroRouteMeta.Fallback ? "true" : "false";
                            Response.Headers["X-NeuroRoute-Duration-Ms"] = chunk.NeuroRouteMeta.DurationMs.ToString("F1");
                        }
                    }
                    var json = JsonSerializer.Serialize(chunk, JsonOptions);
                    await Response.WriteAsync($"data: {json}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
                await Response.WriteAsync("data: [DONE]\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Streaming cancelled by client");
            }
        }
        else
        {
            var response = await _router.RouteAsync(request, ct);

            if (response.NeuroRouteMeta is not null)
            {
                Response.Headers["X-NeuroRoute-Backend"] = response.NeuroRouteMeta.Backend;
                Response.Headers["X-NeuroRoute-Fallback"] = response.NeuroRouteMeta.Fallback ? "true" : "false";
                Response.Headers["X-NeuroRoute-Duration-Ms"] = response.NeuroRouteMeta.DurationMs.ToString("F1");
            }

            var json = JsonSerializer.Serialize(response, JsonOptions);
            Response.ContentType = "application/json";
            await Response.WriteAsync(json, ct);
        }
    }
}
