using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinOptimizer.ViewModels;

namespace WinOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly string? _initialUnlockTarget;

    public MainWindow(string? initialUnlockTarget = null)
    {
        InitializeComponent();
        FitWindowToWorkArea();

        _initialUnlockTarget = initialUnlockTarget;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += OnMainWindowLoaded;
    }

    private void FitWindowToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        double maxWidth = Math.Max(MinWidth, workArea.Width - 36);
        double maxHeight = Math.Max(MinHeight, workArea.Height - 36);

        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);

        Left = workArea.Left + Math.Max(12, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(12, (workArea.Height - Height) / 2);
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_initialUnlockTarget))
        {
            OpenFileUnlocker(_initialUnlockTarget);
        }
        await _viewModel.InitializeAsync();
    }

    public void OpenFileUnlocker(string targetPath)
    {
        FileLockNavRadio.IsChecked = true;
        _viewModel.OpenFileUnlockerWithPath(targetPath);
    }

    private void ConsoleLogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConsoleLogBox.ScrollToEnd();
    }

    private void FileLockPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = _viewModel.ScanFileLocksAsync();
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            OpenFileUnlocker(files[0]);
            e.Handled = true;
        }
    }

    private void HeaderArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignore
            }
        }
    }
}
