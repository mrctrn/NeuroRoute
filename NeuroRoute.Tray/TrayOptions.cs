namespace NeuroRoute.Tray;

public class TrayOptions
{
    public string ServiceEndpoint { get; set; } = "http://localhost:5000";
    public int PollIntervalSeconds { get; set; } = 5;
    public string GpuGuiUrl { get; set; } = "http://localhost:1234";
    public string AdminKey { get; set; } = "";
    public bool AutoStart { get; set; } = true;
}
