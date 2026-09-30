using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserMultiview.Themes;

/// <summary>Small red count badge for the taskbar button overlay (TaskbarItemInfo.Overlay).</summary>
public static class UnreadBadge
{
    // Overlays are shown at small-icon size (16 DIPs); drawn at 32 px so they stay sharp on high DPI.
    private const int Size = 32;
    private static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));

    public static ImageSource Create(int count)
    {
        var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var fontSize = text.Length switch { 1 => 22.0, 2 => 18.0, _ => 13.0 };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(Fill, null, new Point(Size / 2.0, Size / 2.0), Size / 2.0, Size / 2.0);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize, Brushes.White, pixelsPerDip: 1.0);
            dc.DrawText(formatted, new Point((Size - formatted.Width) / 2, (Size - formatted.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
