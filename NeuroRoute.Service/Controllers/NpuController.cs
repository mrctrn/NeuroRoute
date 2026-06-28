using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/admin/npu")]
public sealed class NpuController : ControllerBase
{
    private readonly FlmProcessManager _flmProcessManager;
    private readonly FlmCliService? _flmCliService;
    private readonly IOptions<NeuroRouteOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<NpuController> _logger;

    public NpuController(
        FlmProcessManager flmProcessManager,
        IOptions<NeuroRouteOptions> options,
        IHostEnvironment env,
        ILogger<NpuController> logger,
        FlmCliService? flmCliService = null)
    {
        _flmProcessManager = flmProcessManager;
        _flmCliService = flmCliService;
        _options = options;
        _env = env;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(_flmProcessManager.GetStatus());
    }

    [HttpGet("models")]
    public async Task<IActionResult> ListModels([FromQuery] string filter = "installed")
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        var models = await _flmCliService.ListModelsAsync(filter);
        return Ok(models);
    }

    [HttpPost("pull")]
    public IActionResult PullModel([FromBody] NpuPullRequest request)
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        _logger.LogInformation("Pulling FLM model: {Tag}", request.Tag);

        _ = Task.Run(async () =>
        {
            var success = await _flmCliService.PullModelAsync(request.Tag,
                onOutput: line => _logger.LogInformation("FLM pull: {Line}", line));
            _logger.LogInformation(success
                ? "FLM model {Tag} pulled successfully"
                : "FLM model {Tag} pull failed", request.Tag);
        });

        return Accepted(new { message = $"Pulling model '{request.Tag}' in background" });
    }

    [HttpPost("load")]
    public async Task<IActionResult> LoadModel([FromBody] NpuLoadRequest request)
    {
        _logger.LogInformation("Loading FLM model: {Tag}, ctxLen={CtxLen}, pmode={Pmode}, persist={Persist}",
            request.ModelTag, request.CtxLen, request.Pmode, request.Persist);

        if (request.Persist)
        {
            PersistConfig(request);
        }

        await _flmProcessManager.StopAsync();

        _flmProcessManager.UpdateModel(request.ModelTag, request.CtxLen, request.Pmode);
        await _flmProcessManager.StartAsync(HttpContext.RequestAborted);

        return Ok(new { message = $"Model '{request.ModelTag}' loaded" });
    }

    [HttpDelete("models/{tag}")]
    public async Task<IActionResult> RemoveModel(string tag)
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        _logger.LogInformation("Removing FLM model: {Tag}", tag);
        var success = await _flmCliService.RemoveModelAsync(tag);

        if (!success)
            return StatusCode(500, new { message = $"Failed to remove model '{tag}'" });

        return Ok(new { message = $"Model '{tag}' removed" });
    }

    private void PersistConfig(NpuLoadRequest request)
    {
        try
        {
            var configPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            if (!System.IO.File.Exists(configPath))
            {
                _logger.LogWarning("Cannot persist config: {Path} not found", configPath);
                return;
            }

            var json = System.IO.File.ReadAllText(configPath);
            var node = JsonNode.Parse(json);
            if (node is null) return;

            var neuroRoute = node["NeuroRoute"];
            if (neuroRoute is null) return;

            neuroRoute["NpuFlmModelTag"] = request.ModelTag;
            neuroRoute["NpuFlmCtxLen"] = request.CtxLen;
            neuroRoute["NpuFlmPmode"] = request.Pmode;

            var options = new JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(configPath, node.ToJsonString(options));
            _logger.LogInformation("Config persisted to {Path}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist config changes");
        }
    }
}
