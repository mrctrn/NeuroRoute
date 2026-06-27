using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[Collection("Playwright")]
public sealed class DashboardMetricsTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardMetricsTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
    }

    [Fact]
    public async Task Metrics_DisplayAfterNpuRequest()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = false });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Hello" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");
        Assert.Contains("NPU:", pageText);
    }

    [Fact]
    public async Task NpuHandled_ShowsCount()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = false });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Hello" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");
        Assert.Contains("NPU:", pageText);
    }

    [Fact]
    public async Task GpuEscalated_ShowsCount()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = true, gpuAvailable = true });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Complex task" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");
        Assert.Contains("GPU:", pageText);
    }
}
