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
        WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        WebView.SourceChanged += (_, _) => AddressBox.Text = WebView.Source?.ToString() ?? "";
        WebView.NavigationCompleted += (_, _) => BackButton.IsEnabled = WebView.CanGoBack;

        // Applied by the control once CoreWebView2 initializes; also tracks Ctrl+/Ctrl- zoom.
        WebView.ZoomFactor = config.Zoom;
        WebView.ZoomFactorChanged += (_, _) =>
        {
            Config.Zoom = WebView.ZoomFactor;
            UpdateZoomLabel();
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
        if (!e.IsSuccess)
        {
            Debug.WriteLine($"WebView2 init failed for pane {Config.Id}: {e.InitializationException}");
            WebView.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Não foi possível iniciar o WebView2:\n{e.InitializationException?.Message}";
            ErrorText.Visibility = Visibility.Visible;
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
        WebView.Source = uri;
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
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e) =>
        RemoveRequested?.Invoke(this, EventArgs.Empty);

    public void FocusAddressBox()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    public void Dispose() => WebView.Dispose();
}
