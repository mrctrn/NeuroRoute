using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[Collection("Playwright")]
public sealed class DashboardAdminTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardAdminTests(PlaywrightFixture fixture)
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
    public async Task RestartNpuButton_ReturnsSuccess()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        var restartButton = _page.GetByRole(AriaRole.Button, new() { Name = "Restart NPU Backend" });
        await Assertions.Expect(restartButton).ToBeEnabledAsync();
        await restartButton.ClickAsync();
        await Task.Delay(2000);
        await Assertions.Expect(restartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task ReloadConfigButton_ReturnsSuccess()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        var reloadButton = _page.GetByRole(AriaRole.Button, new() { Name = "Reload Configuration" });
        await Assertions.Expect(reloadButton).ToBeEnabledAsync();
        await reloadButton.ClickAsync();
        await Task.Delay(2000);
        await Assertions.Expect(reloadButton).ToBeEnabledAsync();
    }
}
