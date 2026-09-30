using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BrowserMultiview.Themes;

public static class WindowTheme
{
    /// <summary>
    /// Dark title bar tinted with the theme's surface color. Call from OnSourceInitialized (needs the HWND).
    /// Both DWM attributes need Windows 11 (build 22000+); on older systems the call fails harmlessly.
    /// </summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var useDark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

        var c = ((SolidColorBrush)window.FindResource("SurfaceBrush")).Color;
        var colorRef = c.R | (c.G << 8) | (c.B << 16); // COLORREF is 0x00BBGGRR
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
    }

    /// <summary>
    /// Dark WebView background until the first page finishes loading (no white flash on startup), then the
    /// browser's usual white. DefaultBackgroundColor also shows through pages that don't set a background,
    /// and many assume white: leaving it dark made their (black) text unreadable.
    /// </summary>
    public static void UseDarkBackgroundUntilFirstLoad(WebView2 webView)
    {
        var bg = (Color)webView.FindResource("BackgroundColor");
        webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);

        void OnFirstLoad(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            webView.NavigationCompleted -= OnFirstLoad;
            webView.DefaultBackgroundColor = System.Drawing.Color.White;
        }
        webView.NavigationCompleted += OnFirstLoad;
    }
}
