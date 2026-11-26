using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinOptimizer.Core;

public enum ContextMenuKind
{
    ShellVerb,
    ShellExHandler
}

public sealed class ContextMenuEntry
{
    public required string UniqueId { get; init; }
    public required string DisplayName { get; init; }
    public required string KeyName { get; init; }
    public required string ScopeKey { get; init; }      // DESKTOP | FILE | FOLDER | DRIVE
    public required string ScopeLabel { get; init; }    // 桌面空白处 | 文件 | 文件夹 | 驱动器
    public required ContextMenuKind Kind { get; init; }
    public required RegistryHive Hive { get; init; }
    public required string SubKeyPath { get; init; }
    public string? Clsid { get; init; }
    public bool IsEnabled { get; set; }
    public bool IsProtectedSystemItem { get; init; }

    public string FullRegistryPath => $"{(Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\\{SubKeyPath}";
    public string KindLabel => Kind == ContextMenuKind.ShellVerb ? "Shell 命令" : "ShellEx 扩展";
}

/// <summary>
/// Windows 右键菜单（桌面空白处 / 文件 / 文件夹 / 驱动器）扫描与无损隐藏/恢复管理器。
/// - Shell 命令项：通过写入/移除 Windows 原生 LegacyDisable 值实现零破坏即时隐藏与恢复；
/// - ShellEx 扩展项：优先通过 CLSID 前缀 '-' 实现按场景精确禁用，遇系统权限保护时自动回退至 HKCU Shell Extensions\Blocked。
/// </summary>
public static class ContextMenuManager
{
    private const string BlockedShellExPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static readonly HashSet<string> ProtectedSystemKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "New", "Display", "Personalize", "open", "explore", "find", "copyaspath", "Properties"
    };

    private static readonly Dictionary<string, string> FriendlyHandlerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NvAppDesktopContext"] = "NVIDIA App",
        ["NvCplDesktopContext"] = "NVIDIA 控制面板",
        ["New"] = "新建 (New)",
        ["WorkFolders"] = "工作文件夹",
        ["Sharing"] = "授予访问权限 / 共享",
        ["EPP"] = "Microsoft Defender 扫描",
        ["ModernSharing"] = "共享 (Modern Sharing)",
        ["Open With"] = "打开方式",
        ["CopyAsPathMenu"] = "复制文件地址",
        ["7-Zip"] = "7-Zip 压缩菜单",
        ["WinRAR"] = "WinRAR 压缩菜单",
        ["WinRAR32"] = "WinRAR 压缩菜单",
        ["Bandizip"] = "Bandizip 压缩菜单",
        ["PeaZip"] = "PeaZip 压缩菜单"
    };

    private readonly record struct ScanTarget(
        string ScopeKey,
        string ScopeLabel,
        RegistryHive Hive,
        string RootPath,
        ContextMenuKind Kind);

    private static readonly ScanTarget[] ScanTargets =
    [
        // 桌面与目录空白处
        new("DESKTOP", "桌面空白处", RegistryHive.LocalMachine, @"SOFTWARE\Classes\DesktopBackground\Shell", ContextMenuKind.ShellVerb),
        new("DESKTOP", "桌面空白处", RegistryHive.CurrentUser, @"Software\Classes\DesktopBackground\Shell", ContextMenuKind.ShellVerb),
        new("DESKTOP", "桌面空白处", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\Background\shell", ContextMenuKind.ShellVerb),
        new("DESKTOP", "桌面空白处", RegistryHive.CurrentUser, @"Software\Classes\Directory\Background\shell", ContextMenuKind.ShellVerb),
        new("DESKTOP", "桌面空白处", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),
        new("DESKTOP", "桌面空白处", RegistryHive.CurrentUser, @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),

        // 所有文件 (*)
        new("FILE", "文件 (*)", RegistryHive.LocalMachine, @"SOFTWARE\Classes\*\shell", ContextMenuKind.ShellVerb),
        new("FILE", "文件 (*)", RegistryHive.CurrentUser, @"Software\Classes\*\shell", ContextMenuKind.ShellVerb),
        new("FILE", "文件 (*)", RegistryHive.LocalMachine, @"SOFTWARE\Classes\*\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),
        new("FILE", "文件 (*)", RegistryHive.CurrentUser, @"Software\Classes\*\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),

        // 文件夹 (Directory)
        new("FOLDER", "文件夹", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell", ContextMenuKind.ShellVerb),
        new("FOLDER", "文件夹", RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", ContextMenuKind.ShellVerb),
        new("FOLDER", "文件夹", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),
        new("FOLDER", "文件夹", RegistryHive.CurrentUser, @"Software\Classes\Directory\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler),

        // 驱动器 (Drive)
        new("DRIVE", "驱动器", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Drive\shell", ContextMenuKind.ShellVerb),
        new("DRIVE", "驱动器", RegistryHive.LocalMachine, @"SOFTWARE\Classes\Drive\shellex\ContextMenuHandlers", ContextMenuKind.ShellExHandler)
    ];

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        uint cchOutBuf,
        nint ppvReserved);

    public static Task<List<ContextMenuEntry>> ScanAllAsync()
    {
        return Task.Run(() =>
        {
            var results = new List<ContextMenuEntry>();
            var blockedClsids = LoadBlockedClsids();

            foreach (var target in ScanTargets)
            {
                try
                {
                    using var baseKey = SystemHelper.OpenBaseKey(target.Hive);
                    using var rootKey = baseKey.OpenSubKey(target.RootPath, writable: false);
                    if (rootKey == null) continue;

                    foreach (var subName in rootKey.GetSubKeyNames())
                    {
                        // 忽略内部占位键（如 .SpotlightLearnMore 这种系统私有隐藏键或纯空格分隔符）
                        if (subName.StartsWith('.') || string.IsNullOrWhiteSpace(subName))
                            continue;

                        using var itemKey = rootKey.OpenSubKey(subName, writable: false);
                        if (itemKey == null) continue;

                        string fullSubPath = $@"{target.RootPath}\{subName}";
                        string uid = $"{target.Hive}|{fullSubPath}";

                        if (target.Kind == ContextMenuKind.ShellVerb)
                        {
                            // 跳过仅靠程序内部调用、常规右键根本不显示的隐藏项（无可见命令或标记为仅编程访问）
                            if (itemKey.GetValue("ProgrammaticAccessOnly") != null)
                                continue;

                            bool hasLegacyDisable = itemKey.GetValue("LegacyDisable") != null;
                            string displayName = ResolveShellVerbDisplayName(itemKey, subName);

                            results.Add(new ContextMenuEntry
                            {
                                UniqueId = uid,
                                DisplayName = displayName,
                                KeyName = subName,
                                ScopeKey = target.ScopeKey,
                                ScopeLabel = target.ScopeLabel,
                                Kind = ContextMenuKind.ShellVerb,
                                Hive = target.Hive,
                                SubKeyPath = fullSubPath,
                                IsEnabled = !hasLegacyDisable,
                                IsProtectedSystemItem = ProtectedSystemKeys.Contains(subName)
                            });
                        }
                        else
                        {
                            string rawDefault = itemKey.GetValue(null)?.ToString()?.Trim() ?? string.Empty;
                            // 有些 ShellEx 键名本身就是 CLSID，或者默认值是 CLSID
                            string rawClsid = rawDefault.Contains('{') ? rawDefault : (subName.Contains('{') ? subName : string.Empty);
                            if (string.IsNullOrEmpty(rawClsid)) continue;

                            bool prefixedDisabled = rawClsid.StartsWith('-');
                            string cleanClsid = prefixedDisabled ? rawClsid[1..].Trim() : rawClsid;
                            bool blockedInPolicy = blockedClsids.Contains(cleanClsid);

                            string displayName = ResolveShellExDisplayName(subName, cleanClsid);

                            results.Add(new ContextMenuEntry
                            {
                                UniqueId = uid,
                                DisplayName = displayName,
                                KeyName = subName,
                                ScopeKey = target.ScopeKey,
                                ScopeLabel = target.ScopeLabel,
                                Kind = ContextMenuKind.ShellExHandler,
                                Hive = target.Hive,
                                SubKeyPath = fullSubPath,
                                Clsid = cleanClsid,
                                IsEnabled = !prefixedDisabled && !blockedInPolicy,
                                IsProtectedSystemItem = ProtectedSystemKeys.Contains(subName)
                            });
                        }
                    }
                }
                catch
                {
                    // ignore inaccessible keys
                }
            }

            return results
                .OrderBy(x => x.ScopeKey switch { "DESKTOP" => 0, "FILE" => 1, "FOLDER" => 2, _ => 3 })
                .ThenBy(x => x.IsEnabled ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });
    }

    public static Task<TweakResult> SetEnabledAsync(ContextMenuEntry entry, bool enable, Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                if (entry.Kind == ContextMenuKind.ShellVerb)
                {
                    if (enable)
                    {
                        log($"正在启用右键项「{entry.DisplayName}」(移除 LegacyDisable)...");
                        SystemHelper.DeleteRegistryValue(entry.Hive, entry.SubKeyPath, "LegacyDisable");
                    }
                    else
                    {
                        log($"正在隐藏右键项「{entry.DisplayName}」(写入 LegacyDisable)...");
                        SystemHelper.SetRegistryString(entry.Hive, entry.SubKeyPath, "LegacyDisable", string.Empty);
                    }

                    entry.IsEnabled = enable;
                    return new TweakResult(true, $"已{(enable ? "恢复显示" : "隐藏")}右键菜单项「{entry.DisplayName}」（下次右键即刻生效）。");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(entry.Clsid))
                        return new TweakResult(false, "无法识别该 ShellEx 扩展的 CLSID。");

                    if (enable)
                    {
                        log($"正在启用 ShellEx 右键扩展「{entry.DisplayName}」({entry.Clsid})...");
                        try
                        {
                            var cur = SystemHelper.GetRegistryValue(entry.Hive, entry.SubKeyPath, null)?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(cur) && cur.StartsWith('-'))
                            {
                                SystemHelper.SetRegistryString(entry.Hive, entry.SubKeyPath, null, entry.Clsid);
                            }
                        }
                        catch
                        {
                            // ignore if key is read-only; unblock via HKCU Blocked below
                        }

                        SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, BlockedShellExPath, entry.Clsid);
                        entry.IsEnabled = true;
                        return new TweakResult(true, $"已恢复显示右键扩展「{entry.DisplayName}」。", RecommendExplorerRestart: false);
                    }
                    else
                    {
                        log($"正在隐藏 ShellEx 右键扩展「{entry.DisplayName}」({entry.Clsid})...");
                        bool disabledDirect = false;
                        try
                        {
                            var cur = SystemHelper.GetRegistryValue(entry.Hive, entry.SubKeyPath, null)?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(cur) && cur.StartsWith('{'))
                            {
                                SystemHelper.SetRegistryString(entry.Hive, entry.SubKeyPath, null, "-" + entry.Clsid);
                                disabledDirect = true;
                            }
                        }
                        catch
                        {
                            disabledDirect = false;
                        }

                        if (!disabledDirect)
                        {
                            log("该节点受系统权限保护，已自动切换为无损 HKCU Shell Extensions\\Blocked 屏蔽方案...");
                            SystemHelper.SetRegistryString(RegistryHive.CurrentUser, BlockedShellExPath, entry.Clsid, entry.DisplayName);
                        }

                        entry.IsEnabled = false;
                        return new TweakResult(
                            true,
                            $"已隐藏右键扩展「{entry.DisplayName}」（如桌面菜单仍有缓存，点击左侧「重启资源管理器」即可立刻刷新）。",
                            RecommendExplorerRestart: !disabledDirect);
                    }
                }
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"操作右键项「{entry.DisplayName}」失败：{ex.Message}");
            }
        });
    }

    public static Task<TweakResult> DeleteEntryAsync(ContextMenuEntry entry, Action<string> log)
    {
        return Task.Run(() =>
        {
            if (entry.IsProtectedSystemItem)
                return new TweakResult(false, $"「{entry.DisplayName}」属于系统核心右键项，为防误删已禁止彻底删除（如需不显示请直接关闭右侧开关隐藏）。");

            try
            {
                log($"正在彻底删除右键注册表项 {entry.FullRegistryPath}...");
                SystemHelper.DeleteRegistrySubKeyTree(entry.Hive, entry.SubKeyPath);
                if (!string.IsNullOrWhiteSpace(entry.Clsid))
                {
                    SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, BlockedShellExPath, entry.Clsid);
                }
                return new TweakResult(true, $"已永久删除右键项「{entry.DisplayName}」。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"删除失败（可能受系统权限保护，建议改用左侧开关隐藏）：{ex.Message}");
            }
        });
    }

    private static HashSet<string> LoadBlockedClsids()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var hkcu = SystemHelper.OpenBaseKey(RegistryHive.CurrentUser);
            using var key = hkcu.OpenSubKey(BlockedShellExPath, writable: false);
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                    set.Add(name.Trim());
            }
        }
        catch
        {
            // ignore
        }
        return set;
    }

    private static string ResolveShellVerbDisplayName(RegistryKey itemKey, string fallbackKeyName)
    {
        string? raw = itemKey.GetValue("MUIVerb")?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            raw = itemKey.GetValue(null)?.ToString();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            string resolved = ResolveIndirectString(raw);
            if (!string.IsNullOrWhiteSpace(resolved))
                return CleanAccelerator(resolved);
        }

        return FriendlyHandlerNames.TryGetValue(fallbackKeyName, out var known) ? known : fallbackKeyName;
    }

    private static string ResolveShellExDisplayName(string keyName, string clsid)
    {
        if (FriendlyHandlerNames.TryGetValue(keyName, out var known))
            return known;

        try
        {
            var clsidName = SystemHelper.GetRegistryValue(RegistryHive.ClassesRoot, $@"CLSID\{clsid}", null)?.ToString();
            if (!string.IsNullOrWhiteSpace(clsidName))
            {
                string resolved = ResolveIndirectString(clsidName);
                if (!string.IsNullOrWhiteSpace(resolved) && !resolved.StartsWith('{'))
                    return keyName.StartsWith('{') ? resolved : $"{keyName} ({resolved})";
            }
        }
        catch
        {
            // ignore
        }

        return keyName;
    }

    private static string ResolveIndirectString(string input)
    {
        if (!input.StartsWith('@'))
            return input.Trim();

        var sb = new StringBuilder(512);
        int hr = SHLoadIndirectString(input, sb, (uint)sb.Capacity, 0);
        return hr == 0 && sb.Length > 0 ? sb.ToString().Trim() : input;
    }

    private static string CleanAccelerator(string text)
        => text.Replace("(&", "(").Replace("&", string.Empty).Trim();
}
