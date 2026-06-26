using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Routing;
using Microsoft.AspNetCore.Http;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly Router _router;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(Router router, ILogger<ChatController> logger)
    {
        _router = router;
        _logger = logger;
    }

    [HttpPost("completions")]
    public async Task CreateCompletion([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (request.Stream)
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (var chunk in _router.StreamAsync(request, ct))
                {
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

            var json = JsonSerializer.Serialize(response, JsonOptions);
            Response.ContentType = "application/json";
            await Response.WriteAsync(json, ct);
        }
    }
}
