namespace BrowserMultiview.Models;

public enum PaneOrientation
{
    /// <summary>Panes side by side (columns).</summary>
    Horizontal,
    /// <summary>Panes stacked (rows).</summary>
    Vertical,
}

public sealed class WorkspaceConfig
{
    public PaneOrientation Orientation { get; set; } = PaneOrientation.Horizontal;

    public WindowBounds? Window { get; set; }

    public bool IsMaximized { get; set; }

    /// <summary>Hide the toolbar and pane bars until the mouse reaches the top edge (like taskbar auto-hide).</summary>
    public bool AutoHideBars { get; set; } = true;

    public List<PaneConfig> Panes { get; set; } = [];

    public static WorkspaceConfig CreateDefault() => new()
    {
        Orientation = PaneOrientation.Horizontal,
        Panes =
        [
            new PaneConfig { Title = "WhatsApp 1", Url = "https://web.whatsapp.com" },
            new PaneConfig { Title = "WhatsApp 2", Url = "https://web.whatsapp.com" },
        ],
    };
}

/// <summary>Restore bounds of the window, in WPF device-independent units.</summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height);
