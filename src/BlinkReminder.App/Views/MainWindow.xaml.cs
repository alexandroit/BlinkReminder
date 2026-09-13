using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BlinkReminder.App.ViewModels;

namespace BlinkReminder.App.Views;

public partial class MainWindow : Window
{
    private readonly Func<bool> canHide;
    public bool IsExiting { get; set; }
    public MainWindow(MainViewModel viewModel, Func<bool> canHide)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.canHide = canHide;
        Closing += OnClosing;
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (IsExiting) return;
        args.Cancel = true;
        if (canHide()) Hide();
        else ((MainViewModel)DataContext).Feedback = Localizer.Current["TrayUnavailable"];
    }

    private async void SaveClick(object sender, RoutedEventArgs args)
    {
        var viewModel = (MainViewModel)DataContext;
        // Update the last focused editor before checking its binding validation.
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var invalid = FindInvalid(this);
        if (invalid is not null)
        {
            viewModel.ReportInvalidInput();
            invalid.Focus();
            return;
        }
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try { await viewModel.SaveAsync(); }
            finally { button.IsEnabled = true; }
        }
    }

    private static UIElement? FindInvalid(DependencyObject parent)
    {
        if (parent is UIElement element && Validation.GetHasError(parent)) return element;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var invalid = FindInvalid(VisualTreeHelper.GetChild(parent, index));
            if (invalid is not null) return invalid;
        }
        return null;
    }
}
