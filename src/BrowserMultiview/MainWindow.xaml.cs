using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BrowserMultiview.Models;
using BrowserMultiview.Services;

namespace BrowserMultiview;

public partial class MainWindow : Window
{
    // Thin on purpose; it is also the whole grab area, since the WebView2 HWNDs next to it take the mouse.
    private const double SplitterThickness = 3;

    private readonly WorkspaceConfig _config;
    private readonly List<PaneView> _panes = [];

    public MainWindow()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => SaveNow();

        AppPaths.EnsureCreated();
        _config = WorkspaceStore.Load();
        ApplySavedPlacement();

        foreach (var paneConfig in _config.Panes)
            AttachPane(new PaneView(paneConfig));

        NormalizeSizes();
        Relayout();

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
        AutoHideCheckBox.IsChecked = _config.AutoHideBars;
        ApplyAutoHideMode();
    }

    private void AttachPane(PaneView pane)
    {
        pane.RemoveRequested += Pane_RemoveRequested;
        pane.ConfigChanged += Pane_ConfigChanged;
        _panes.Add(pane);
        PanesHost.Children.Add(pane);
    }

    /// <summary>
    /// Rebuilds row/column definitions and splitters around the existing panes.
    /// Panes are never removed from PanesHost here: reparenting a WebView2 reloads it.
    /// </summary>
    private void Relayout()
    {
        foreach (var splitter in PanesHost.Children.OfType<GridSplitter>().ToList())
            PanesHost.Children.Remove(splitter);
        PanesHost.RowDefinitions.Clear();
        PanesHost.ColumnDefinitions.Clear();

        var horizontal = _config.Orientation == PaneOrientation.Horizontal;
        for (var i = 0; i < _panes.Count; i++)
        {
            if (i > 0)
            {
                AddTrack(horizontal, new GridLength(SplitterThickness));
                var splitter = new GridSplitter
                {
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = SystemColors.ControlDarkBrush,
                    // The preview adorner would be drawn under the WebView2 HWNDs (airspace); resize live instead.
                    ShowsPreview = false,
                };
                splitter.DragCompleted += (_, _) => RequestSave();
                PlaceInTrack(splitter, horizontal, TrackCount(horizontal) - 1);
                PanesHost.Children.Add(splitter);
            }

            AddTrack(horizontal, new GridLength(_panes[i].Config.Size, GridUnitType.Star));
            PlaceInTrack(_panes[i], horizontal, TrackCount(horizontal) - 1);
        }

        EmptyText.Visibility = _panes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddTrack(bool horizontal, GridLength length)
    {
        if (horizontal)
            PanesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        else
            PanesHost.RowDefinitions.Add(new RowDefinition { Height = length });
    }

    private int TrackCount(bool horizontal) =>
        horizontal ? PanesHost.ColumnDefinitions.Count : PanesHost.RowDefinitions.Count;

    private static void PlaceInTrack(UIElement element, bool horizontal, int index)
    {
        Grid.SetColumn(element, horizontal ? index : 0);
        Grid.SetRow(element, horizontal ? 0 : index);
    }

    /// <summary>
    /// Copies the current on-screen sizes (after splitter drags) into the pane configs as relative weights.
    /// Pane i lives in track 2*i; odd tracks are splitters.
    /// </summary>
    private void CaptureSizes()
    {
        var horizontal = _config.Orientation == PaneOrientation.Horizontal;
        for (var i = 0; i < _panes.Count; i++)
        {
            var track = 2 * i;
            if (track >= TrackCount(horizontal))
                return;

            var length = horizontal ? PanesHost.ColumnDefinitions[track].ActualWidth : PanesHost.RowDefinitions[track].ActualHeight;
            if (length > 0)
                _panes[i].Config.Size = length;
        }

        NormalizeSizes();
    }

    /// <summary>Rescales weights so they average 1.0; weights are then unit-free and valid on either axis.</summary>
    private void NormalizeSizes()
    {
        if (_panes.Count == 0)
            return;

        var mean = _panes.Average(p => p.Config.Size);
        foreach (var pane in _panes)
            pane.Config.Size = mean > 0 ? pane.Config.Size / mean : 1.0;
    }

    private void AddPaneButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureSizes();

        var config = new PaneConfig { Title = $"Painel {_panes.Count + 1}", Size = 1.0 };
        _config.Panes.Add(config);
        var pane = new PaneView(config);
        AttachPane(pane);

        NormalizeSizes();
        Relayout();
        RequestSave();
        pane.FocusAddressBox();
    }

    private void ToggleLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureSizes();
        _config.Orientation = _config.Orientation == PaneOrientation.Horizontal
            ? PaneOrientation.Vertical
            : PaneOrientation.Horizontal;
        Relayout();
        RequestSave();
    }

    private void Pane_RemoveRequested(object? sender, EventArgs e)
    {
        if (sender is not PaneView pane)
            return;

        var name = string.IsNullOrWhiteSpace(pane.Config.Title) ? pane.Config.Url : pane.Config.Title;
        var answer = MessageBox.Show(this,
            $"Remover o painel \"{name}\"?",
            "Remover painel", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
            return;

        CaptureSizes();

        pane.RemoveRequested -= Pane_RemoveRequested;
        pane.ConfigChanged -= Pane_ConfigChanged;
        _panes.Remove(pane);
        _config.Panes.Remove(pane.Config);
        PanesHost.Children.Remove(pane);
        pane.Dispose();
        // TODO: delete the removed pane's WebView2 profile data (WV2Profile_pane-<id>); it stays on disk for now.

        NormalizeSizes();
        Relayout();
        RequestSave();
    }

    #region Bar auto-hide

    // WPF cannot draw over the WebView2 HWNDs (airspace), so revealed bars push the pages down
    // instead of overlaying them. The cursor is polled because mouse events over a WebView2
    // go to the browser's own window, not to WPF.
    private const double RevealZone = 6;                               // DIPs from a top edge
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);

    private readonly DispatcherTimer _autoHideTimer;
    private bool _barsVisible = true;
    private DateTime _pendingSince = DateTime.MinValue;

    private void AutoHideCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoHideBars = AutoHideCheckBox.IsChecked == true;
        ApplyAutoHideMode();
        RequestSave();
    }

    private void ApplyAutoHideMode()
    {
        if (_config.AutoHideBars)
        {
            _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
            SetBarsVisible(true);
        }
    }

    private void SetBarsVisible(bool visible)
    {
        _barsVisible = visible;
        _pendingSince = DateTime.MinValue;
        Toolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pane in _panes)
            pane.SetBarVisible(visible);
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        // Never change layout mid-interaction: splitter drag, open menu, typing in an address box.
        // (Only text boxes count: a clicked button keeps focus and would pin the bars forever.)
        if (Mouse.Captured is not null || Keyboard.FocusedElement is TextBox)
        {
            _pendingSince = DateTime.MinValue;
            return;
        }

        var wanted = CursorOverOurWindow(out var cursor) && (_barsVisible ? IsOverBars(cursor) : IsInRevealZone(cursor));
        if (wanted == _barsVisible)
        {
            _pendingSince = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (_pendingSince == DateTime.MinValue)
            _pendingSince = now;
        else if (now - _pendingSince >= (wanted ? ShowDelay : HideDelay))
            SetBarsVisible(wanted);
    }

    /// <summary>True when the topmost window under the cursor belongs to this window (including WebView2 children).</summary>
    private bool CursorOverOurWindow(out Point cursor)
    {
        cursor = default;
        if (!NativeMethods.GetCursorPos(out var screen))
            return false;

        var hwnd = new WindowInteropHelper(this).Handle;
        var under = NativeMethods.WindowFromPoint(screen);
        if (hwnd == IntPtr.Zero || NativeMethods.GetAncestor(under, NativeMethods.GA_ROOT) != hwnd)
            return false;

        cursor = PointFromScreen(new Point(screen.X, screen.Y));
        return true;
    }

    private bool IsInRevealZone(Point cursor)
    {
        if (cursor.Y >= 0 && cursor.Y < RevealZone)
            return true;

        return _panes.Any(p => BoundsInWindow(p) is { } r &&
            cursor.X >= r.Left && cursor.X < r.Right && cursor.Y >= r.Top && cursor.Y < r.Top + RevealZone);
    }

    private bool IsOverBars(Point cursor)
    {
        if (cursor.Y >= 0 && cursor.Y < RevealZone)
            return true;

        return new FrameworkElement[] { Toolbar }.Concat(_panes.Select(p => p.Bar))
            .Any(bar => BoundsInWindow(bar) is { } r && r.Contains(cursor));
    }

    private Rect? BoundsInWindow(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0)
            return null;
        return element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
    }

    private static class NativeMethods
    {
        public const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        public const uint MONITOR_DEFAULTTONULL = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    }

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        SaveNow();
    }

    #region Window placement

    private const double DefaultWidth = 1400;
    private const double DefaultHeight = 800;

    // Last non-minimized state, so closing while minimized restores maximized/normal correctly.
    private bool _wasMaximized;

    /// <summary>Applies saved bounds before the window is shown; <see cref="OnSourceInitialized"/> validates them.</summary>
    private void ApplySavedPlacement()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (_config.Window is { } b)
        {
            Left = b.Left;
            Top = b.Top;
            Width = b.Width;
            Height = b.Height;
        }
        else
        {
            ApplyDefaultPlacement();
        }

        _wasMaximized = _config.IsMaximized;
        LocationChanged += (_, _) => RequestSave();
        SizeChanged += (_, _) => RequestSave();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                _wasMaximized = WindowState == WindowState.Maximized;
            RequestSave();
        };
    }

    /// <summary>Default size, clamped to the primary work area and centered in it.</summary>
    private void ApplyDefaultPlacement()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(DefaultWidth, area.Width * 0.9);
        Height = Math.Min(DefaultHeight, area.Height * 0.9);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The monitor setup may have changed since the bounds were saved. The virtual screen is only the
        // bounding box of all monitors (it has holes when they differ in size or are offset), so instead
        // ask Windows whether the title bar strip, in physical pixels, touches a real monitor.
        if (_config.Window is not null && !TitleBarIsOnAMonitor())
            ApplyDefaultPlacement();

        if (_config.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private bool TitleBarIsOnAMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var r))
            return true; // can't tell; keep the saved placement

        const int stripHeight = 30, inset = 40;
        var strip = new NativeMethods.RECT
        {
            Left = r.Left + inset,
            Top = r.Top,
            Right = Math.Max(r.Left + inset + 1, r.Right - inset),
            Bottom = r.Top + stripHeight,
        };
        return NativeMethods.MonitorFromRect(ref strip, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }

    private void CaptureWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (!bounds.IsEmpty && double.IsFinite(bounds.Left) && bounds.Width > 0 && bounds.Height > 0)
            _config.Window = new WindowBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        _config.IsMaximized = _wasMaximized;
    }

    #endregion

    #region Saving

    // Saved shortly after every change, not only on close: stopping the debugger or killing
    // the process skips OnClosing, which used to lose zoom and layout changes.
    private readonly DispatcherTimer _saveTimer;

    private void Pane_ConfigChanged(object? sender, EventArgs e) => RequestSave();

    /// <summary>Debounced save: bursts of changes (e.g. Ctrl+wheel zoom) produce a single write.</summary>
    private void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        CaptureSizes();
        CaptureWindowPlacement();
        try
        {
            WorkspaceStore.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the layout is not worth crashing or blocking the user over.
            Debug.WriteLine($"Failed to save workspace: {ex}");
        }
    }

    #endregion
}
