namespace BrowserMultiview.Services;

public static class UrlPolicy
{
    /// <summary>
    /// Accepts only absolute http/https URLs. Input without "://" gets "https://" prepended,
    /// so "example.com" works and "javascript:..." / "file:..." are rejected.
    /// </summary>
    public static bool TryNormalize(string? input, out Uri uri)
    {
        uri = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || !IsWebScheme(parsed) || string.IsNullOrEmpty(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }

    public static bool IsWebScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
}
