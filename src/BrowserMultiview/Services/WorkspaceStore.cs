using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrowserMultiview.Models;

namespace BrowserMultiview.Services;

public static class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads the workspace. A missing, unreadable or corrupt file never throws:
    /// a corrupt file is set aside as *.corrupt-&lt;timestamp&gt; and the default config is returned.
    /// <paramref name="fromFile"/> is false whenever the default was used instead of the saved file.
    /// </summary>
    public static WorkspaceConfig Load(out bool fromFile, string? path = null)
    {
        fromFile = false;
        path ??= AppPaths.WorkspaceFile;
        if (!File.Exists(path))
            return WorkspaceConfig.CreateDefault();

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<WorkspaceConfig>(json, JsonOptions);
            if (config is null)
                return WorkspaceConfig.CreateDefault();
            fromFile = true;
            return Sanitize(config);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Debug.WriteLine($"Failed to load workspace '{path}': {ex}");
            SetAsideCorrupt(path);
            return WorkspaceConfig.CreateDefault();
        }
    }

    /// <summary>Writes to a temp file in the same folder, then replaces the target in one move.</summary>
    public static void Save(WorkspaceConfig config, string? path = null)
    {
        path ??= AppPaths.WorkspaceFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    private static WorkspaceConfig Sanitize(WorkspaceConfig config)
    {
        config.Panes ??= [];
        config.Panes.RemoveAll(p => p is null);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pane in config.Panes)
        {
            // Ids name the WebView2 profile; replace anything missing, duplicated or unsafe.
            if (string.IsNullOrWhiteSpace(pane.Id) || !pane.Id.All(char.IsAsciiLetterOrDigit) || !seenIds.Add(pane.Id))
                pane.Id = PaneConfig.NewId();

            pane.Title ??= "";
            if (string.IsNullOrWhiteSpace(pane.Url))
                pane.Url = "about:blank";
            if (!double.IsFinite(pane.Size) || pane.Size <= 0)
                pane.Size = 1.0;
            pane.Zoom = double.IsFinite(pane.Zoom) && pane.Zoom > 0
                ? Math.Clamp(pane.Zoom, PaneConfig.MinZoom, PaneConfig.MaxZoom)
                : 1.0;
        }

        if (!Enum.IsDefined(config.Orientation))
            config.Orientation = PaneOrientation.Horizontal;

        if (config.Window is { } w &&
            !(double.IsFinite(w.Left) && double.IsFinite(w.Top) && w.Width > 0 && w.Height > 0 &&
              double.IsFinite(w.Width) && double.IsFinite(w.Height)))
            config.Window = null;

        return config;
    }

    private static void SetAsideCorrupt(string path)
    {
        try
        {
            File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not set aside corrupt workspace '{path}': {ex}");
        }
    }
}
