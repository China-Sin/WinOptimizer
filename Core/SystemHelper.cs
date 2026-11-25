using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WinOptimizer.Core;

public static class SystemHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static RegistryKey OpenBaseKey(RegistryHive hive)
        => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    public static object? GetRegistryValue(RegistryHive hive, string subKeyPath, string? valueName)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.OpenSubKey(subKeyPath, writable: false);
        return subKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static bool SubKeyExists(RegistryHive hive, string subKeyPath)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.OpenSubKey(subKeyPath, writable: false);
        return subKey != null;
    }

    public static void SetRegistryDword(RegistryHive hive, string subKeyPath, string valueName, int value)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.CreateSubKey(subKeyPath, writable: true);
        subKey.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public static void SetRegistryString(RegistryHive hive, string subKeyPath, string? valueName, string value)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.CreateSubKey(subKeyPath, writable: true);
        subKey.SetValue(valueName, value, RegistryValueKind.String);
    }

    public static void SetRegistryBinary(RegistryHive hive, string subKeyPath, string valueName, byte[] value)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.CreateSubKey(subKeyPath, writable: true);
        subKey.SetValue(valueName, value, RegistryValueKind.Binary);
    }

    public static void DeleteRegistryValue(RegistryHive hive, string subKeyPath, string valueName)
    {
        using var baseKey = OpenBaseKey(hive);
        using var subKey = baseKey.OpenSubKey(subKeyPath, writable: true);
        if (subKey?.GetValue(valueName) != null)
        {
            subKey.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    public static void DeleteRegistrySubKeyTree(RegistryHive hive, string subKeyPath)
    {
        using var baseKey = OpenBaseKey(hive);
        baseKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// 执行 PowerShell 命令并实时回调日志输出
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(
        string script,
        Action<string>? onOutput = null,
        int timeoutSeconds = 90)
    {
        var wrappedScript = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; {script}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedScript));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                stdout.AppendLine(e.Data);
                onOutput?.Invoke(e.Data.Trim());
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (-1, stdout.ToString(), "执行超时，已终止子进程。");
        }

        return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    /// <summary>
    /// 安全重启 Windows 资源管理器（使右键菜单、任务栏等壳层设置立即生效）
    /// </summary>
    public static async Task RestartExplorerAsync(Action<string>? log = null)
    {
        log?.Invoke("正在重启 Windows 资源管理器 (explorer.exe)...");
        await Task.Run(async () =>
        {
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch
                {
                    // ignore individual handle errors
                }
                finally
                {
                    proc.Dispose();
                }
            }

            await Task.Delay(600);

            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = true
                });
            }
        });
        log?.Invoke("Windows 资源管理器重启完成。");
    }

    public static string GetWindowsVersionSummary()
    {
        try
        {
            var productName = GetRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName")?.ToString() ?? "Windows";
            var displayVersion = GetRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")?.ToString() ?? "";
            var currentBuild = GetRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")?.ToString() ?? "";
            var ubr = GetRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR")?.ToString() ?? "";

            if (int.TryParse(currentBuild, out var buildNum) && buildNum >= 22000 && productName.Contains("Windows 10"))
            {
                productName = productName.Replace("Windows 10", "Windows 11");
            }

            return $"{productName} {displayVersion} (Build {currentBuild}.{ubr})".Trim();
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }
}
