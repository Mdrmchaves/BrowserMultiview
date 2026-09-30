using System.Diagnostics;
using System.Windows;
using BrowserMultiview.Services;
using BrowserMultiview.Themes;
using Microsoft.Web.WebView2.Core;

namespace BrowserMultiview;

/// <summary>
/// Hosts a window.open() popup (typically an OAuth login) inside the app, on the opener's profile,
/// so the page keeps its window.opener link and the login lands in the right pane's session.
/// </summary>
public partial class PopupWindow : Window
{
    private const double MinPopupSize = 320, MaxPopupWidth = 1200, MaxPopupHeight = 900;

    public PopupWindow(Window owner, CoreWebView2WindowFeatures? features)
    {
        InitializeComponent();
        Owner = owner;
        WindowTheme.UseDarkBackgroundUntilFirstLoad(WebView);

        if (features is { HasSize: true })
        {
            // Requested size is for the page; add room for the address strip.
            Width = Math.Clamp(features.Width, MinPopupSize, MaxPopupWidth);
            Height = Math.Clamp(features.Height + 40, MinPopupSize, MaxPopupHeight);
        }

        Closed += (_, _) => WebView.Dispose();
    }

    public CoreWebView2 CoreWebView2 => WebView.CoreWebView2;

    /// <summary>
    /// Initializes on the opener's environment and profile, as NewWindow requires. Must be awaited while
    /// the NewWindowRequested deferral is held, and before anything navigates this WebView.
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment, string profileName)
    {
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = profileName;
        await WebView.EnsureCoreWebView2Async(environment, options);

        var core = WebView.CoreWebView2;
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Popup" : core.DocumentTitle;
        core.SourceChanged += (_, _) => ShowAddress(core.Source);
        core.WindowCloseRequested += (_, _) => Close(); // window.close() at the end of a login flow
        core.NewWindowRequested += (_, e) =>
        {
            // No popup-in-popup: anything further goes to the system browser.
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && UrlPolicy.IsWebScheme(uri))
                ExternalBrowser.Open(uri);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void ShowAddress(string source)
    {
        AddressText.Text = source;
        var secure = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        LockIcon.Text = secure ? "" : ""; // Lock / Warning
        LockIcon.ToolTip = secure ? "Conexão segura (https)" : "Conexão não segura";
        Debug.WriteLine($"Popup at {source}");
    }
}
