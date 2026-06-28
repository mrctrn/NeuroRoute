using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuroRoute.Service.Controllers;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NSubstitute;

namespace NeuroRoute.Tests.Controllers;

public sealed class NpuControllerTests
{
    [Fact]
    public void GetStatus_ReturnsOk_WithFlmStatus()
    {
        var flmClient = new FlmClient(new HttpClient(), NullLogger<FlmClient>.Instance);
        var processManager = new FlmProcessManager(
            "gemma4-it:e4b", "127.0.0.1", 52625, 0, "performance",
            flmClient, NullLogger<FlmProcessManager>.Instance);

        var options = Options.Create(new NeuroRouteOptions());
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(Directory.GetCurrentDirectory());
        var logger = NullLogger<NpuController>.Instance;

        var controller = new NpuController(options, env, logger, flmProcessManager: processManager);
        var result = controller.GetStatus();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<FlmStatus>(okResult.Value);
        Assert.Equal("gemma4-it:e4b", status.ModelTag);
        Assert.Equal("127.0.0.1", status.Host);
        Assert.Equal(52625, status.Port);
    }

    [Fact]
    public void GetStatus_Returns503_WhenFlmProcessManagerNotAvailable()
    {
        var options = Options.Create(new NeuroRouteOptions());
        var env = Substitute.For<IHostEnvironment>();
        var logger = NullLogger<NpuController>.Instance;

        var controller = new NpuController(options, env, logger);
        var result = controller.GetStatus();

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusCodeResult.StatusCode);
    }
}
