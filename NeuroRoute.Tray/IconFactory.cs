using System.Drawing;

namespace NeuroRoute.Tray;

public static class IconFactory
{
    private static readonly Dictionary<string, Icon> Cache = [];

    public static Icon GetIcon(string state)
    {
        if (Cache.TryGetValue(state, out var cached))
            return cached;

        var color = state.ToLowerInvariant() switch
        {
            "green" => Color.FromArgb(76, 175, 80),
            "yellow" => Color.FromArgb(255, 193, 7),
            "red" => Color.FromArgb(244, 67, 54),
            _ => Color.FromArgb(158, 158, 158)
        };

        Cache[state] = CreateCircleIcon(color);
        return Cache[state];
    }

    private static Icon CreateCircleIcon(Color color, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, size - 2, size - 2);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
