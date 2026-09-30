using System.Diagnostics;

namespace BrowserMultiview.Services;

public static class ExternalBrowser
{
    /// <summary>Opens an http/https URL in the system's default browser. Other schemes are ignored.</summary>
    public static void Open(Uri uri)
    {
        if (!UrlPolicy.IsWebScheme(uri))
        {
            Debug.WriteLine($"Refusing to open non-web URL externally: {uri}");
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
}
