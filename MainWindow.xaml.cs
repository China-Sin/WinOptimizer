using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinOptimizer.ViewModels;

namespace WinOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        FitWindowToWorkArea();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += OnMainWindowLoaded;
    }

    /// <summary>
    /// 根据当前显示器可用工作区（自动适配 150%/175%/200% 高 DPI 缩放）自适应窗口尺寸并居中，
    /// 保证顶部标题栏永远完整处于屏幕可见范围内，绝不顶出屏幕上沿。
    /// </summary>
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
        await _viewModel.InitializeAsync();
    }

    private void ConsoleLogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConsoleLogBox.ScrollToEnd();
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
                // ignore if mouse released before DragMove completes
            }
        }
    }
}
