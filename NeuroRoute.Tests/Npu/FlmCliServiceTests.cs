using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;

namespace NeuroRoute.Tests.Npu;

public sealed class FlmCliServiceTests
{
    [Fact]
    public async Task ListModelsAsync_ReturnsEmptyList_WhenFlmNotAvailable()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<FlmCliService>();
        var service = new FlmCliService("nonexistent-flm.exe", logger);
        var result = await service.ListModelsAsync();
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
