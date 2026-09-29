using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BrowserMultiview.Models;
using BrowserMultiview.Services;
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
        // Dark instead of the default white while a page loads (matches BackgroundColor in Themes/Dark.xaml).
        var bg = (System.Windows.Media.Color)FindResource("BackgroundColor");
        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
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
        }
        else
        {
            Debug.WriteLine($"WebView2 init failed for pane {Config.Id}: {e.InitializationException}");
            WebView.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Não foi possível iniciar o WebView2:\n{e.InitializationException?.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Links/window.open() that ask for a new window go to the system's default browser (http/https only);
    /// anything else is dropped. Handled=true without NewWindow closes the popup immediately.
    /// REVIEW: this breaks sites whose login uses an OAuth popup and waits for it via window.opener
    /// (the popup opens outside the app and can never report back). If a site needs that, handle it by
    /// setting e.NewWindow to a WebView2 in the same profile instead.
    /// </summary>
    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !UrlPolicy.IsWebScheme(uri))
        {
            Debug.WriteLine($"Blocked new window for non-web URL: {e.Uri}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open {uri} in the default browser: {ex}");
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

    public void SetBarVisible(bool visible) =>
        NavBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void FocusAddressBox()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    public void Dispose() => WebView.Dispose();
}
