using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using System.Diagnostics;

namespace NeuroRoute.Tray;

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _npuStatusItem;
    private readonly ToolStripMenuItem _gpuStatusItem;
    private readonly HealthPoller _poller;
    private readonly ServiceClient _client;
    private readonly TrayOptions _options;
    private readonly SynchronizationContext _uiContext;

    public TrayContext()
    {
        _options = LoadOptions();
        _client = new ServiceClient(_options.ServiceEndpoint, _options.AdminKey);
        _poller = new HealthPoller(_client, TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        _menu = new ContextMenuStrip();

        _menu.Items.Add("Open Dashboard", null, (_, _) => OpenUrl(_options.ServiceEndpoint));
        _menu.Items.Add("Open GPU Backend GUI", null, (_, _) => OpenUrl(_options.GpuGuiUrl));
        _menu.Items.Add(new ToolStripSeparator());

        _statusItem = new ToolStripMenuItem("Status: \u25CF Checking...") { Enabled = false };
        _npuStatusItem = new ToolStripMenuItem("NPU: checking...") { Enabled = false };
        _gpuStatusItem = new ToolStripMenuItem("GPU: checking...") { Enabled = false };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(_npuStatusItem);
        _menu.Items.Add(_gpuStatusItem);
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("Restart NPU Backend", null, async (_, _) => await AdminAction("restart-backend"));
        _menu.Items.Add("Reload Configuration", null, async (_, _) => await AdminAction("reload-config"));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("View Logs", null, (_, _) => OpenEventViewer());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Stop Service", null, async (_, _) => await AdminAction("stop"));
        _menu.Items.Add("Exit NeuroRoute Tray", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = IconFactory.GetIcon("gray"),
            Text = "NeuroRoute \u2014 Starting...",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenUrl(_options.ServiceEndpoint);

        _poller.OnHealthUpdated += UpdateUi;
        _poller.Start();

        if (_options.AutoStart)
            EnsureAutoStart();
    }

    private TrayOptions LoadOptions()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(Application.ExecutablePath)!)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var opts = new TrayOptions();
        config.GetSection("NeuroRoute").Bind(opts);
        return opts;
    }

    private void UpdateUi(object? sender, HealthResult health)
    {
        _uiContext.Post(_ =>
        {
            _trayIcon.Icon = IconFactory.GetIcon(health.IconState);
            _trayIcon.Text = $"NeuroRoute \u2014 {health.Status}";

            _statusItem.Text = $"Status: {health.StatusIcon} {health.Status}";
            _npuStatusItem.Text = $"NPU: {health.NpuIcon} {health.NpuStatus}{(string.IsNullOrEmpty(health.NpuModel) ? "" : $" ({health.NpuModel})")}";
            _gpuStatusItem.Text = $"GPU: {health.GpuIcon} {health.GpuStatus}{(string.IsNullOrEmpty(health.GpuModel) ? "" : $" ({health.GpuModel})")}";
        }, null);
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void OpenEventViewer()
    {
        Process.Start("eventvwr", "/c:NeuroRoute");
    }

    private async Task AdminAction(string action)
    {
        try
        {
            await _client.PostAsync($"admin/{action}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Action failed: {ex.Message}", "NeuroRoute",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void EnsureAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key?.GetValue("NeuroRoute.Tray") is null)
        {
            key?.SetValue("NeuroRoute.Tray", $"\"{Application.ExecutablePath}\"");
        }
    }

    private void ExitApp()
    {
        _poller.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poller.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
