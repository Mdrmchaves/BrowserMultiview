using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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

    /// <summary>Theme background as a System.Drawing color, for WebView2.DefaultBackgroundColor.</summary>
    public static System.Drawing.Color WebViewBackground(FrameworkElement anyElement)
    {
        var bg = (Color)anyElement.FindResource("BackgroundColor");
        return System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
    }
}
