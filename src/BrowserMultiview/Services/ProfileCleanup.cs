using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace BrowserMultiview.Services;

/// <summary>
/// Removes WebView2 profile folders of panes that no longer exist.
/// Normal removals go through CoreWebView2Profile.Delete(); this sweep catches leftovers
/// (panes removed by older builds, or a removal that happened before the pane finished initializing).
/// </summary>
public static partial class ProfileCleanup
{
    // WebView2 stores profile "<name>" in "<user data folder>\EBWebView\WV2Profile_<name>". That layout is
    // observed, not documented, so only folders matching our own "pane-<32 hex>" naming are ever touched.
    [GeneratedRegex("^WV2Profile_pane-([0-9a-f]{32})$", RegexOptions.IgnoreCase)]
    private static partial Regex PaneProfileFolder();

    /// <summary>
    /// Must run before any WebView2 starts (the browser process locks profile files).
    /// Only call it with ids from a workspace that was really loaded from disk: with a default
    /// (fresh) workspace every existing profile would look orphaned.
    /// </summary>
    public static void DeleteOrphans(IEnumerable<string> paneIdsToKeep)
    {
        var root = Path.Combine(AppPaths.WebViewData, "EBWebView");
        if (!Directory.Exists(root))
            return;

        var keep = new HashSet<string>(paneIdsToKeep, StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var match = PaneProfileFolder().Match(Path.GetFileName(dir));
            if (!match.Success || keep.Contains(match.Groups[1].Value))
                continue;

            try
            {
                Directory.Delete(dir, recursive: true);
                Debug.WriteLine($"Deleted orphaned profile folder {dir}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not delete orphaned profile folder {dir}: {ex.Message}");
            }
        }
    }
}
