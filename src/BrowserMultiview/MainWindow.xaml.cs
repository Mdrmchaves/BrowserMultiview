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
    private readonly WorkspaceConfig _config;

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureCreated();
        _config = WorkspaceStore.Load();

        // Temporary: plain columns until the splitter layout lands.
        for (var i = 0; i < _config.Panes.Count; i++)
        {
            PanesHost.ColumnDefinitions.Add(new ColumnDefinition());
            var pane = new PaneView(_config.Panes[i]);
            Grid.SetColumn(pane, i);
            PanesHost.Children.Add(pane);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

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
