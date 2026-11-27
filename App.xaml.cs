using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace WinOptimizer;

public partial class App : Application
{
    private const string MutexName = @"Global\WinOptimizer_SingleInstance_Mutex_8C8D2AC4";
    private const string ActivateEventName = @"Global\WinOptimizer_BringToFront_Event_8C8D2AC4";
    private static readonly string HandoffFilePath = Path.Combine(Path.GetTempPath(), "WinOptimizer_unlock_handoff.txt");

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _listenerCts;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        string? unlockTarget = ParseUnlockArgument(e.Args);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!string.IsNullOrWhiteSpace(unlockTarget))
            {
                try { File.WriteAllText(HandoffFilePath, unlockTarget); } catch { }
            }

            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(ActivateEventName);
                existingEvent.Set();
            }
            catch
            {
                // ignore
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

        var mainWindow = new MainWindow(unlockTarget);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static string? ParseUnlockArgument(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--unlock", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
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

                string? pendingUnlockPath = null;
                try
                {
                    if (File.Exists(HandoffFilePath))
                    {
                        pendingUnlockPath = File.ReadAllText(HandoffFilePath).Trim();
                        File.Delete(HandoffFilePath);
                    }
                }
                catch
                {
                    // ignore
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow == null) return;

                    if (MainWindow.WindowState == WindowState.Minimized)
                        MainWindow.WindowState = WindowState.Normal;

                    if (!MainWindow.IsVisible)
                        MainWindow.Show();

                    MainWindow.Activate();
                    MainWindow.Topmost = true;
                    MainWindow.Topmost = false;
                    MainWindow.Focus();

                    var hwnd = new WindowInteropHelper(MainWindow).Handle;
                    if (hwnd != 0)
                        SetForegroundWindow(hwnd);

                    if (!string.IsNullOrWhiteSpace(pendingUnlockPath) && MainWindow is MainWindow mw)
                    {
                        mw.OpenFileUnlocker(pendingUnlockPath);
                    }
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
            // ignore
        }

        base.OnExit(e);
    }
}
