namespace BrowserMultiview.Models;

public sealed class PaneConfig
{
    /// <summary>Stable id; also names the WebView2 profile ("pane-&lt;id&gt;"), so it must never change.</summary>
    public string Id { get; set; } = NewId();

    public string Title { get; set; } = "";

    public string Url { get; set; } = "about:blank";

    /// <summary>Relative (star) weight along the workspace orientation.</summary>
    public double Size { get; set; } = 1.0;

    /// <summary>Page zoom for this pane (1.0 = 100%). Also follows Ctrl+/Ctrl- inside the pane.</summary>
    public double Zoom { get; set; } = 1.0;

    public const double MinZoom = 0.25;
    public const double MaxZoom = 5.0;

    // 32 hex chars: safe for ProfileName (ASCII letters/digits only).
    public static string NewId() => Guid.NewGuid().ToString("N");
}
