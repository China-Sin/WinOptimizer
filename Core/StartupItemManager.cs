using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinOptimizer.Core;

public enum StartupSourceKind
{
    RegistryRun,
    StartupFolder
}

public sealed class StartupEntry
{
    public required string UniqueId { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string ExecutablePath { get; init; }
    public required bool TargetFileExists { get; init; }
    public required StartupSourceKind SourceKind { get; init; }
    public required string SourceLabel { get; init; }
    public required RegistryHive Hive { get; init; }
    public string? RunKeyPath { get; init; }
    public required string ApprovedKeyPath { get; init; }
    public required string ValueOrFileName { get; init; }
    public string? ShortcutFilePath { get; init; }
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Windows 开机启动项扫描与管理引擎（与系统任务管理器「启动应用」100% 同步）。
/// 支持 HKCU/HKLM Run、WOW6432Node Run32 及用户/公共启动文件夹，
/// 通过原生 StartupApproved 12 字节二进制状态实现零破坏启用/禁用，并支持文件定位与彻底清理残留项。
/// </summary>
public static class StartupItemManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32Path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static Task<List<StartupEntry>> ScanAllAsync()
    {
        return Task.Run(() =>
        {
            var results = new List<StartupEntry>();

            // 1. 当前用户注册表 Run
            ScanRegistryRun(results, RegistryHive.CurrentUser, RunPath, ApprovedRunPath, "当前用户注册表");

            // 2. 系统全局 64 位注册表 Run
            ScanRegistryRun(results, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "系统全局注册表");

            // 3. 系统全局 32 位注册表 WOW6432Node Run
            ScanRegistryRun(results, RegistryHive.LocalMachine, Run32Path, ApprovedRun32Path, "系统 32 位注册表");

            // 4. 当前用户启动文件夹
            string userStartupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ScanStartupFolder(results, userStartupDir, RegistryHive.CurrentUser, ApprovedFolderPath, "用户启动文件夹");

            // 5. 公共启动文件夹
            string commonStartupDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ScanStartupFolder(results, commonStartupDir, RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "公共启动文件夹");

            return results
                .OrderBy(x => x.IsEnabled ? 0 : 1)
                .ThenBy(x => x.TargetFileExists ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });
    }

    public static Task<TweakResult> SetEnabledAsync(StartupEntry entry, bool enable, Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                byte[] stateBytes = BuildStartupApprovedBytes(enable);
                log($"正在{(enable ? "启用" : "禁用")}开机启动项「{entry.Name}」(同步写入 StartupApproved)...");
                SystemHelper.SetRegistryBinary(entry.Hive, entry.ApprovedKeyPath, entry.ValueOrFileName, stateBytes);
                entry.IsEnabled = enable;

                return new TweakResult(true, $"已{(enable ? "允许开机自启" : "禁用开机自启")}「{entry.Name}」（与系统任务管理器已实时同步）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"设置开机启动项「{entry.Name}」失败：{ex.Message}");
            }
        });
    }

    public static Task<TweakResult> DeleteEntryAsync(StartupEntry entry, Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                if (entry.SourceKind == StartupSourceKind.RegistryRun && !string.IsNullOrEmpty(entry.RunKeyPath))
                {
                    log($"正在从注册表删除启动项「{entry.Name}」...");
                    SystemHelper.DeleteRegistryValue(entry.Hive, entry.RunKeyPath, entry.ValueOrFileName);
                }
                else if (entry.SourceKind == StartupSourceKind.StartupFolder && !string.IsNullOrEmpty(entry.ShortcutFilePath))
                {
                    log($"正在删除启动文件夹快捷方式 {entry.ShortcutFilePath}...");
                    if (File.Exists(entry.ShortcutFilePath))
                        File.Delete(entry.ShortcutFilePath);
                }

                // 同步清理 StartupApproved 中的对应残留记录
                SystemHelper.DeleteRegistryValue(entry.Hive, entry.ApprovedKeyPath, entry.ValueOrFileName);

                return new TweakResult(true, $"已彻底删除开机启动项「{entry.Name}」。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"删除开机启动项「{entry.Name}」失败：{ex.Message}");
            }
        });
    }

    public static void OpenFileLocation(StartupEntry entry)
    {
        string target = !string.IsNullOrWhiteSpace(entry.ShortcutFilePath) && File.Exists(entry.ShortcutFilePath)
            ? entry.ShortcutFilePath
            : entry.ExecutablePath;

        if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target}\"",
                UseShellExecute = true
            });
        }
        else if (!string.IsNullOrWhiteSpace(target))
        {
            string? dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
        }
    }

    private static void ScanRegistryRun(
        List<StartupEntry> results,
        RegistryHive hive,
        string runKeyPath,
        string approvedKeyPath,
        string sourceLabel)
    {
        try
        {
            using var baseKey = SystemHelper.OpenBaseKey(hive);
            using var runKey = baseKey.OpenSubKey(runKeyPath, writable: false);
            if (runKey == null) return;

            var approvedMap = LoadApprovedStates(hive, approvedKeyPath);

            foreach (var valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string command = runKey.GetValue(valueName)?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(command)) continue;

                bool isEnabled = !approvedMap.TryGetValue(valueName, out bool approvedEnabled) || approvedEnabled;
                string exePath = ExtractExecutablePath(command);
                bool fileExists = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath);

                results.Add(new StartupEntry
                {
                    UniqueId = $"{hive}|{runKeyPath}|{valueName}",
                    Name = valueName.Trim(),
                    Command = command,
                    ExecutablePath = exePath,
                    TargetFileExists = fileExists,
                    SourceKind = StartupSourceKind.RegistryRun,
                    SourceLabel = sourceLabel,
                    Hive = hive,
                    RunKeyPath = runKeyPath,
                    ApprovedKeyPath = approvedKeyPath,
                    ValueOrFileName = valueName,
                    IsEnabled = isEnabled
                });
            }
        }
        catch
        {
            // ignore inaccessible keys
        }
    }

    private static void ScanStartupFolder(
        List<StartupEntry> results,
        string folderPath,
        RegistryHive hive,
        string approvedKeyPath,
        string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        try
        {
            var approvedMap = LoadApprovedStates(hive, approvedKeyPath);

            foreach (var filePath in Directory.EnumerateFiles(folderPath))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isEnabled = !approvedMap.TryGetValue(fileName, out bool approvedEnabled) || approvedEnabled;
                string displayName = Path.GetFileNameWithoutExtension(fileName);

                results.Add(new StartupEntry
                {
                    UniqueId = $"FOLDER|{filePath}",
                    Name = displayName,
                    Command = filePath,
                    ExecutablePath = filePath,
                    TargetFileExists = File.Exists(filePath),
                    SourceKind = StartupSourceKind.StartupFolder,
                    SourceLabel = sourceLabel,
                    Hive = hive,
                    ApprovedKeyPath = approvedKeyPath,
                    ValueOrFileName = fileName,
                    ShortcutFilePath = filePath,
                    IsEnabled = isEnabled
                });
            }
        }
        catch
        {
            // ignore inaccessible folders
        }
    }

    private static Dictionary<string, bool> LoadApprovedStates(RegistryHive hive, string approvedKeyPath)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = SystemHelper.OpenBaseKey(hive);
            using var key = baseKey.OpenSubKey(approvedKeyPath, writable: false);
            if (key == null) return map;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] bytes && bytes.Length >= 4)
                {
                    // Windows StartupApproved 规范：首字节偶数 (0x02/0x00/0x06) = 启用；奇数 (0x03/0x01/0x07) = 禁用
                    bool enabled = (bytes[0] & 1) == 0;
                    map[name] = enabled;
                }
            }
        }
        catch
        {
            // ignore
        }
        return map;
    }

    private static byte[] BuildStartupApprovedBytes(bool enable)
    {
        var bytes = new byte[12];
        if (enable)
        {
            bytes[0] = 0x02;
        }
        else
        {
            bytes[0] = 0x03;
            long fileTime = DateTime.UtcNow.ToFileTimeUtc();
            BitConverter.GetBytes(fileTime).CopyTo(bytes, 4);
        }
        return bytes;
    }

    private static string ExtractExecutablePath(string rawCommand)
    {
        string expanded = Environment.ExpandEnvironmentVariables(rawCommand.Trim());
        if (string.IsNullOrWhiteSpace(expanded))
            return string.Empty;

        // 情况 1：带双引号的路径，如 "C:\Program Files\App\app.exe" --arg
        if (expanded.StartsWith('"'))
        {
            int endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
                return expanded[1..endQuote];
        }

        // 情况 2：无引号但包含 .exe / .bat / .cmd / .lnk / .vbs 后缀（即使路径中带空格也能准确提取）
        foreach (string ext in new[] { ".exe", ".lnk", ".bat", ".cmd", ".vbs", ".ps1" })
        {
            int idx = expanded.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                string candidate = expanded[..(idx + ext.Length)].Trim('"');
                if (File.Exists(candidate))
                    return candidate;
                return candidate;
            }
        }

        // 兜底：取首个空格前字段
        int firstSpace = expanded.IndexOf(' ');
        return firstSpace > 0 ? expanded[..firstSpace] : expanded;
    }
}
