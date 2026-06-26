using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[CollectionDefinition("Playwright")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }

[Collection("Playwright")]
public sealed class DashboardHealthTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardHealthTests(PlaywrightFixture fixture)
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
    public async Task BothHealthy_ShowsGreen()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("healthy", statusBadge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NpuDown_ShowsDegradedOverall()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("degraded", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task GpuDown_ShowsDegradedOverall()
    {
        await _fixture.ProgramScenarioAsync(new { gpuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("degraded", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task BothDown_ShowsUnhealthy()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false, gpuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("unhealthy", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }
}
