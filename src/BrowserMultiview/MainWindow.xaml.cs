using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BrowserMultiview.Models;
using BrowserMultiview.Services;
using BrowserMultiview.Themes;

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
        _config = WorkspaceStore.Load(out var loadedFromFile);
        if (loadedFromFile)
            ProfileCleanup.DeleteOrphans(_config.Panes.Select(p => p.Id)); // before any WebView2 starts
        ApplySavedPlacement();

        foreach (var paneConfig in _config.Panes)
            AttachPane(new PaneView(paneConfig));

        NormalizeSizes();
        Relayout();

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
        AutoHideCheckBox.IsChecked = _config.AutoHideBars;
        ApplyAutoHideMode();

        // Open popups do not follow the window; close them when it moves, resizes or loses the foreground.
        LocationChanged += (_, _) => HideOverlayBars();
        SizeChanged += (_, _) => HideOverlayBars();
        StateChanged += (_, _) => HideOverlayBars();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!NativeMethods.IsOwnWindow(NativeMethods.GetForegroundWindow()))
                HideOverlayBars();
        }, DispatcherPriority.Background);
    }

    private void AttachPane(PaneView pane)
    {
        pane.RemoveRequested += Pane_RemoveRequested;
        pane.ConfigChanged += Pane_ConfigChanged;
        pane.SetOverlayMode(OverlayMode);
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
                    Style = (Style)FindResource("PaneSplitter"),
                    // The preview adorner would be drawn under the WebView2 HWNDs (airspace); resize live instead.
                    ShowsPreview = false,
                };
                splitter.DragCompleted += (_, _) => { RequestSave(); RefreshOverlayBarsAfterLayout(); };
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
        if (OverlayMode)
        {
            // The new pane's bar is a popup: open the bars (after layout) so its address box can take focus.
            Dispatcher.BeginInvoke(() => { ShowOverlayBars(); pane.FocusAddressBox(); }, DispatcherPriority.Loaded);
        }
        else
        {
            pane.FocusAddressBox();
        }
    }

    private void ToggleLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureSizes();
        _config.Orientation = _config.Orientation == PaneOrientation.Horizontal
            ? PaneOrientation.Vertical
            : PaneOrientation.Horizontal;
        Relayout();
        RequestSave();
        RefreshOverlayBarsAfterLayout();
    }

    private void Pane_RemoveRequested(object? sender, EventArgs e)
    {
        if (sender is not PaneView pane)
            return;

        var name = string.IsNullOrWhiteSpace(pane.Config.Title) ? pane.Config.Url : pane.Config.Title;
        if (!ConfirmDialog.Show(this, "Remover painel",
                $"Remover o painel \"{name}\"?",
                "Os dados deste painel (login, cookies e cache) também serão apagados. Não dá para desfazer.",
                "Remover"))
            return;

        CaptureSizes();

        pane.RemoveRequested -= Pane_RemoveRequested;
        pane.ConfigChanged -= Pane_ConfigChanged;
        _panes.Remove(pane);
        _config.Panes.Remove(pane.Config);
        PanesHost.Children.Remove(pane);
        pane.DeleteProfileAndDispose();

        NormalizeSizes();
        Relayout();
        RequestSave();
        RefreshOverlayBarsAfterLayout();
    }

    #region Bar auto-hide (overlay)

    // With auto-hide on, the toolbar and pane bars live in popups drawn *over* the pages: WPF content
    // cannot overlap a WebView2 (airspace), but a popup is its own top-level HWND and can. They are shown
    // when the cursor reaches the top edge of the window or of a pane. The cursor is polled because mouse
    // events over a WebView2 go to the browser's window, not to WPF. All geometry is in screen coordinates
    // (GetCursorPos / PointToScreen use the same space) because the bars are not in this window's tree.
    // With auto-hide off, the bars are docked above the pages and always visible.
    private const double RevealZone = 6;                               // DIPs from a top edge
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);

    private readonly DispatcherTimer _autoHideTimer;
    private bool _barsVisible;
    private DateTime _pendingSince = DateTime.MinValue;

    private bool OverlayMode => _config.AutoHideBars;

    private void AutoHideCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoHideBars = AutoHideCheckBox.IsChecked == true;
        // The checkbox itself lives in the toolbar being moved between popup and dock: switch after the click.
        Dispatcher.BeginInvoke(ApplyAutoHideMode, DispatcherPriority.Background);
        RequestSave();
    }

    private void ApplyAutoHideMode()
    {
        HideOverlayBars();
        SetToolbarOverlay(OverlayMode);
        foreach (var pane in _panes)
            pane.SetOverlayMode(OverlayMode);

        if (OverlayMode)
            _autoHideTimer.Start();
        else
            _autoHideTimer.Stop();
    }

    private void SetToolbarOverlay(bool overlay)
    {
        ToolbarPopup.IsOpen = false;
        if (overlay && ToolbarPopup.Child is null)
        {
            ToolbarSlot.Child = null;
            ToolbarPopup.Child = Toolbar;
        }
        else if (!overlay && ToolbarSlot.Child is null)
        {
            ToolbarPopup.Child = null;
            Toolbar.Width = double.NaN;
            ToolbarSlot.Child = Toolbar;
        }
    }

    /// <summary>Opens (or re-positions) all overlay bars: toolbar across the top, each pane's bar at its top edge.</summary>
    private void ShowOverlayBars()
    {
        if (!OverlayMode)
            return;

        _barsVisible = true;
        _pendingSince = DateTime.MinValue;

        Toolbar.Width = ContentArea.ActualWidth;
        ToolbarPopup.IsOpen = false;
        ToolbarPopup.IsOpen = true;
        // Only laid out once its popup is open; measuring it while closed yields 0.
        Toolbar.UpdateLayout();
        var toolbarHeight = Toolbar.ActualHeight;

        foreach (var pane in _panes)
        {
            // Panes touching the top of the window get their bar just below the toolbar.
            var top = pane.TranslatePoint(new Point(0, 0), ContentArea).Y;
            pane.ShowOverlayBar(top < 1 ? toolbarHeight : 0);
        }
    }

    private void HideOverlayBars()
    {
        _barsVisible = false;
        _pendingSince = DateTime.MinValue;
        ToolbarPopup.IsOpen = false;
        foreach (var pane in _panes)
            pane.HideOverlayBar();
    }

    /// <summary>After panes move (add/remove/toggle), open popups must be re-positioned; they don't follow layout.</summary>
    private void RefreshOverlayBarsAfterLayout()
    {
        if (_barsVisible)
            Dispatcher.BeginInvoke(ShowOverlayBars, DispatcherPriority.Loaded);
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        // Never change anything mid-interaction: splitter drag, open menu, typing in an address box.
        // (Only text boxes count: a clicked button keeps focus and would pin the bars forever.)
        if (Mouse.Captured is not null || Keyboard.FocusedElement is TextBox)
        {
            _pendingSince = DateTime.MinValue;
            return;
        }

        // Popups are topmost windows: only show them while this app is in the foreground.
        var wanted = NativeMethods.IsOwnWindow(NativeMethods.GetForegroundWindow())
            && CursorOverOurWindows(out var cursor)
            && (_barsVisible ? IsOverBars(cursor) : IsInRevealZone(cursor));
        if (wanted == _barsVisible)
        {
            _pendingSince = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (_pendingSince == DateTime.MinValue)
            _pendingSince = now;
        else if (now - _pendingSince >= (wanted ? ShowDelay : HideDelay))
        {
            if (wanted)
                ShowOverlayBars();
            else
                HideOverlayBars();
        }
    }

    /// <summary>True when the window under the cursor is ours: this window (incl. WebView2 children) or one of our popups.</summary>
    private static bool CursorOverOurWindows(out Point cursor)
    {
        cursor = default;
        if (!NativeMethods.GetCursorPos(out var screen))
            return false;

        cursor = new Point(screen.X, screen.Y);
        var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(screen), NativeMethods.GA_ROOT);
        return NativeMethods.IsOwnWindow(root);
    }

    private bool IsInRevealZone(Point cursor) =>
        TopStripOnScreen(ContentArea) is { } windowTop && windowTop.Contains(cursor)
        || _panes.Any(p => TopStripOnScreen(p) is { } paneTop && paneTop.Contains(cursor));

    private bool IsOverBars(Point cursor) =>
        IsInRevealZone(cursor)
        || new FrameworkElement[] { Toolbar }.Concat(_panes.Select(p => p.Bar))
            .Any(bar => BoundsOnScreen(bar) is { } r && r.Contains(cursor));

    private static Rect? TopStripOnScreen(FrameworkElement element) => BoundsOnScreen(element, RevealZone);

    /// <summary>Element bounds in screen coordinates, or null when it is not currently shown.</summary>
    private static Rect? BoundsOnScreen(FrameworkElement element, double? height = null)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || PresentationSource.FromVisual(element) is null)
            return null;
        return new Rect(
            element.PointToScreen(new Point(0, 0)),
            element.PointToScreen(new Point(element.ActualWidth, height ?? element.ActualHeight)));
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
        WindowTheme.ApplyDarkTitleBar(this);

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
