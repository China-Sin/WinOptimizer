using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace WinOptimizer;

public partial class App : Application
{
    private const string MutexName = @"Global\WinOptimizer_SingleInstance_Mutex_8C8D2AC4";
    private const string ActivateEventName = @"Global\WinOptimizer_BringToFront_Event_8C8D2AC4";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _listenerCts;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // 已有实例运行：向主实例发送唤醒信号将其置顶到前台，当前副实例静默退出
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(ActivateEventName);
                existingEvent.Set();
            }
            catch
            {
                // ignore if primary instance is still initializing or exiting
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _listenerCts = new CancellationTokenSource();
        StartBringToFrontListener(_listenerCts.Token);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void StartBringToFrontListener(CancellationToken token)
    {
        Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activateEvent!, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(handles);
                if (signaled != 0) break;

                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow == null) return;

                    if (MainWindow.WindowState == WindowState.Minimized)
                        MainWindow.WindowState = WindowState.Normal;

                    if (!MainWindow.IsVisible)
                        MainWindow.Show();

                    MainWindow.Activate();
                    // 短暂置顶再取消，突破 Windows 前台焦点锁定确保弹窗回到最前
                    MainWindow.Topmost = true;
                    MainWindow.Topmost = false;
                    MainWindow.Focus();

                    var hwnd = new WindowInteropHelper(MainWindow).Handle;
                    if (hwnd != 0)
                        SetForegroundWindow(hwnd);
                });
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
            _activateEvent?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // ignore cleanup errors on exit
        }

        base.OnExit(e);
    }
}
