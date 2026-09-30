using System.Windows;
using BrowserMultiview.Themes;

namespace BrowserMultiview;

/// <summary>Dark-themed replacement for MessageBox for destructive confirmations. Enter confirms, Esc cancels.</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(Window owner, string title, string message, string detail, string confirmText)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        MessageText.Text = message;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmText;
        // Focus the safe choice: an accidental Enter/Space should not destroy anything.
        Loaded += (_, _) => CancelButton.Focus();
    }

    public static bool Show(Window owner, string title, string message, string detail, string confirmText) =>
        new ConfirmDialog(owner, title, message, detail, confirmText).ShowDialog() == true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
