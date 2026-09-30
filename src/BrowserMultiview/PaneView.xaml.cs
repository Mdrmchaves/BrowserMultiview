using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BrowserMultiview.Models;
using BrowserMultiview.Services;
using BrowserMultiview.Themes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BrowserMultiview;

/// <summary>
/// One browser pane: a WebView2 bound to its own profile plus a small navigation bar.
/// Must stay in the visual tree for its whole life; reparenting a WebView2 reloads/blanks it.
/// </summary>
public partial class PaneView : UserControl, IDisposable
{
    public PaneConfig Config { get; }

    public event EventHandler? RemoveRequested;

    /// <summary>Raised when something in <see cref="Config"/> changed (URL, zoom) and should be saved.</summary>
    public event EventHandler? ConfigChanged;

    public PaneView(PaneConfig config)
    {
        Config = config;
        InitializeComponent();

        // All panes share one user data folder (one browser process); the profile
        // is what isolates cookies/storage. Must be set before the control initializes.
        WebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = AppPaths.WebViewData,
            ProfileName = ProfileNameFor(config),
        };
        // Dark instead of the default white while a page loads.
        WindowTheme.UseDarkBackgroundUntilFirstLoad(WebView);
        WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        WebView.SourceChanged += (_, _) => AddressBox.Text = WebView.Source?.ToString() ?? "";
        WebView.NavigationCompleted += (_, _) => BackButton.IsEnabled = WebView.CanGoBack;

        // Applied by the control once CoreWebView2 initializes; also tracks Ctrl+/Ctrl- zoom.
        WebView.ZoomFactor = config.Zoom;
        WebView.ZoomFactorChanged += (_, _) =>
        {
            Config.Zoom = WebView.ZoomFactor;
            UpdateZoomLabel();
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        };
        UpdateZoomLabel();

        AddressBox.Text = config.Url;
        if (UrlPolicy.TryNormalize(config.Url, out var start))
            WebView.Source = start; // triggers implicit initialization with CreationProperties
        else
            _ = WebView.EnsureCoreWebView2Async();
    }

    /// <summary>
    /// Profile names allow ASCII letters, digits and a few symbols, max 64 chars, case-insensitive.
    /// Pane ids are 32 hex chars, so "pane-&lt;id&gt;" is always valid.
    /// </summary>
    public static string ProfileNameFor(PaneConfig config) => $"pane-{config.Id}";

    private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            WebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            WebView.CoreWebView2.DocumentTitleChanged += (_, _) => UpdateUnreadCount(WebView.CoreWebView2.DocumentTitle);
            UpdateUnreadCount(WebView.CoreWebView2.DocumentTitle);
        }
        else
        {
            Debug.WriteLine($"WebView2 init failed for pane {Config.Id}: {e.InitializationException}");
            WebView.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Não foi possível iniciar o WebView2:\n{e.InitializationException?.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Unread count the page reports in its title, or 0.</summary>
    public int UnreadCount { get; private set; }

    public event EventHandler? UnreadCountChanged;

    // Sites like WhatsApp Web put the unread count in the title as "(3) WhatsApp". Reading the title
    // needs no script injection. Whatever the site leaves out (e.g. muted chats) is not counted.
    [GeneratedRegex(@"^\s*\((\d+)\)")]
    private static partial Regex TitleUnreadCount();

    private void UpdateUnreadCount(string? title)
    {
        var match = TitleUnreadCount().Match(title ?? "");
        var count = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
        if (count == UnreadCount)
            return;

        UnreadCount = count;
        UnreadCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly List<PopupWindow> _popups = [];

    /// <summary>
    /// Requests for a new window:
    /// - window.open() with an explicit size (how OAuth/login popups are opened) → in-app popup on this
    ///   pane's profile, so the opener gets its window.opener link and the login lands in this session;
    /// - everything else (target=_blank links, plain window.open) → system default browser, http/https only.
    /// Other schemes are dropped. Handled=true without NewWindow closes the popup immediately.
    /// REVIEW: the size heuristic is what separates the two cases; a site that opens its login popup
    /// without a size would still go to the external browser and lose window.opener.
    /// </summary>
    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        var isSizedPopup = e.WindowFeatures?.HasSize == true;
        var uriOk = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

        if (isSizedPopup && uriOk && (UrlPolicy.IsWebScheme(uri!) || e.Uri == "about:blank"))
        {
            await OpenInAppPopupAsync(e);
            return;
        }

        if (uriOk && UrlPolicy.IsWebScheme(uri!))
            ExternalBrowser.Open(uri!);
        else
            Debug.WriteLine($"Blocked new window for non-web URL: {e.Uri}");
    }

    private async Task OpenInAppPopupAsync(CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Setting NewWindow must wait for the popup's WebView to initialize, so hold the request open.
        var deferral = e.GetDeferral();
        var popup = new PopupWindow(Window.GetWindow(this), e.WindowFeatures);
        _popups.Add(popup);
        popup.Closed += (_, _) => _popups.Remove(popup);
        popup.Show(); // the WebView2 needs a live window to initialize

        try
        {
            await popup.InitializeAsync(WebView.CoreWebView2.Environment, ProfileNameFor(Config));
            e.NewWindow = popup.CoreWebView2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Debug.WriteLine($"In-app popup failed for pane {Config.Id}: {ex}");
            popup.Close();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Navigate(string input)
    {
        if (!UrlPolicy.TryNormalize(input, out var uri))
        {
            AddressBox.Text = WebView.Source?.ToString() ?? Config.Url;
            return;
        }

        // Persist what the user chose as this pane's URL, not every in-page redirect.
        Config.Url = uri.ToString();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        WebView.Source = uri;
        WebView.Focus();
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Navigate(AddressBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddressBox.Text = WebView.Source?.ToString() ?? Config.Url;
            WebView.Focus();
            e.Handled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CanGoBack)
            WebView.GoBack();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.Reload();
        else
            Navigate(Config.Url);
    }

    private static readonly double[] ZoomPresets = [0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5];

    private void UpdateZoomLabel() => ZoomButton.Content = $"{Math.Round(Config.Zoom * 100)}%";

    private void ZoomButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ZoomButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var preset in ZoomPresets)
        {
            var item = new MenuItem
            {
                Header = $"{Math.Round(preset * 100)}%",
                IsCheckable = true,
                IsChecked = Math.Abs(preset - Config.Zoom) < 0.005,
            };
            item.Click += (_, _) => SetZoom(preset);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetZoom(double zoom)
    {
        Config.Zoom = Math.Clamp(zoom, PaneConfig.MinZoom, PaneConfig.MaxZoom);
        WebView.ZoomFactor = Config.Zoom;
        UpdateZoomLabel();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e) =>
        RemoveRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The navigation bar, for the window's auto-hide logic.</summary>
    public FrameworkElement Bar => NavBar;

    /// <summary>
    /// Overlay mode moves the bar out of the layout into a popup drawn over the page (shown on demand);
    /// otherwise the bar is docked above the page and always visible. Only the bar moves, never the WebView.
    /// </summary>
    public void SetOverlayMode(bool overlay)
    {
        BarPopup.IsOpen = false;
        if (overlay && BarPopup.Child is null)
        {
            BarSlot.Child = null;
            BarPopup.Child = NavBar;
        }
        else if (!overlay && BarSlot.Child is null)
        {
            BarPopup.Child = null;
            NavBar.Width = double.NaN;
            BarSlot.Child = NavBar;
        }
    }

    /// <summary>Opens (or re-positions) the overlay bar at <paramref name="topOffset"/> DIPs below the pane's top.</summary>
    public void ShowOverlayBar(double topOffset)
    {
        NavBar.Width = WebHost.ActualWidth;
        BarPopup.VerticalOffset = topOffset;
        // Reopening makes the popup recompute its screen position (it does not follow layout changes).
        BarPopup.IsOpen = false;
        BarPopup.IsOpen = true;
    }

    public void HideOverlayBar() => BarPopup.IsOpen = false;

    public void FocusAddressBox()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    /// <summary>
    /// Permanently removes this pane's browsing data (logins, cookies, cache) and closes the pane.
    /// WebView2 deletes the profile folder when the browser process exits, retrying on later starts
    /// if files are still locked. If the WebView never initialized, ProfileCleanup catches it at startup.
    /// </summary>
    public void DeleteProfileAndDispose()
    {
        try
        {
            WebView.CoreWebView2?.Profile.Delete();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"Profile.Delete failed for pane {Config.Id}: {ex.Message}");
        }
        Dispose();
    }

    public void Dispose()
    {
        foreach (var popup in _popups.ToList())
            popup.Close();
        BarPopup.IsOpen = false;
        WebView.Dispose();
    }
}
