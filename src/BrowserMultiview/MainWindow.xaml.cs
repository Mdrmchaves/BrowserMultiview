using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BrowserMultiview.Models;
using BrowserMultiview.Services;

namespace BrowserMultiview;

public partial class MainWindow : Window
{
    private const double SplitterThickness = 6;

    private readonly WorkspaceConfig _config;
    private readonly List<PaneView> _panes = [];

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureCreated();
        _config = WorkspaceStore.Load();

        foreach (var paneConfig in _config.Panes)
            AttachPane(new PaneView(paneConfig));

        NormalizeSizes();
        Relayout();
    }

    private void AttachPane(PaneView pane)
    {
        pane.RemoveRequested += Pane_RemoveRequested;
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
        pane.FocusAddressBox();
    }

    private void ToggleLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureSizes();
        _config.Orientation = _config.Orientation == PaneOrientation.Horizontal
            ? PaneOrientation.Vertical
            : PaneOrientation.Horizontal;
        Relayout();
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
        _panes.Remove(pane);
        _config.Panes.Remove(pane.Config);
        PanesHost.Children.Remove(pane);
        pane.Dispose();
        // TODO: delete the removed pane's WebView2 profile data (WV2Profile_pane-<id>); it stays on disk for now.

        NormalizeSizes();
        Relayout();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        CaptureSizes();
        try
        {
            WorkspaceStore.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the layout is not worth blocking the user from closing the app.
            Debug.WriteLine($"Failed to save workspace: {ex}");
        }
    }
}
