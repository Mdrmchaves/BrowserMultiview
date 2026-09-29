using System.IO;

namespace BrowserMultiview.Services;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\BrowserMultiview</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrowserMultiview");

    /// <summary>Single WebView2 user data folder shared by all panes (each pane uses its own profile).</summary>
    public static string WebViewData { get; } = Path.Combine(Root, "webview");

    public static string WorkspaceFile { get; } = Path.Combine(Root, "workspace.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WebViewData);
    }
}
