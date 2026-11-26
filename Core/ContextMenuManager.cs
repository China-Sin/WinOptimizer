using System.IO;
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
/// Windows 右键菜单（桌面空白处 / 文件 / 文件夹 / 驱动器）所见即所得扫描与无损隐藏/恢复管理器。
/// 自动过滤 Extended (Shift+右键项)、HideBasedOnVelocityId (系统屏蔽项)、AppliesTo (特定目录条件项)、
/// 孤立失效 DLL 及 OneDrive/系统内部占位扩展，只展示平时普通右键真正可见的项目。
/// </summary>
public static class ContextMenuManager
{
    private const string BlockedShellExPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static readonly HashSet<string> ProtectedSystemKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "New", "Display", "Personalize", "open", "explore", "find", "copyaspath", "Properties"
    };

    /// <summary>
    /// 系统内部隐藏占位或仅在特殊目录（如 OneDrive 同步目录 / 脱机文件 / 贴纸）才触发的条件型项，普通右键不可见
    /// </summary>
    private static readonly HashSet<string> HiddenInternalKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EditStickers", "FileSyncEx", "WorkFolders", "Offline Files", "EncryptionContextMenuHandler",
        "Library Location", "PlayTo", "Open Containing Folder Menu", "UpdateEncryptionSettings",
        "UpdateEncryptionSettingsWork", "Sharing", "ModernSharing", "PintoStartScreen", "bvshell"
    };

    private static readonly HashSet<string> HiddenInternalClsids = new(StringComparer.OrdinalIgnoreCase)
    {
        "{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}", // OneDrive FileSyncEx
        "{596AB062-B4D2-4215-9F74-E9109B0A8153}", // Previous Versions
        "{F81E9010-6EA4-11CE-A7FF-00AA003CA9F6}", // Sharing
        "{90AA3A4E-1CBA-4233-B8BB-535773D48449}", // TaskbarPin
        "{A2A9545D-A0C2-42B4-9708-A0B2BADD77C8}"  // StartMenuPin
    };

    private static readonly Dictionary<string, string> FriendlyHandlerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NvAppDesktopContext"] = "NVIDIA App",
        ["NvCplDesktopContext"] = "NVIDIA 控制面板",
        ["New"] = "新建 (W)",
        ["EPP"] = "Microsoft Defender 扫描",
        ["Open With"] = "打开方式",
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
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        string trimmedName = subName.Trim();
                        if (string.IsNullOrEmpty(trimmedName) || trimmedName.StartsWith('.'))
                            continue;
                        if (HiddenInternalKeyNames.Contains(trimmedName))
                            continue;

                        using var itemKey = rootKey.OpenSubKey(subName, writable: false);
                        if (itemKey == null) continue;

                        string fullSubPath = $@"{target.RootPath}\{subName}";
                        string uid = $"{target.Hive}|{fullSubPath}";

                        if (target.Kind == ContextMenuKind.ShellVerb)
                        {
                            // 过滤平时普通右键不会显示的隐藏项：
                            // 1. Extended (仅按住 Shift + 右键才显示)
                            // 2. ProgrammaticAccessOnly (仅程序内部调用)
                            // 3. HideBasedOnVelocityId (被 Windows 新版本功能开关隐藏)
                            // 4. AppliesTo (仅匹配特定文件路径/条件才显示)
                            if (itemKey.GetValue("Extended") != null ||
                                itemKey.GetValue("ProgrammaticAccessOnly") != null ||
                                itemKey.GetValue("HideBasedOnVelocityId") != null ||
                                itemKey.GetValue("AppliesTo") != null)
                                continue;

                            // 必须具备真实命令子键或二级子菜单，且具备可显示名称
                            bool hasCommand = itemKey.OpenSubKey("command") != null ||
                                              itemKey.GetValue("SubCommands") != null ||
                                              itemKey.OpenSubKey("shell") != null;
                            bool hasCaption = !string.IsNullOrWhiteSpace(itemKey.GetValue("MUIVerb")?.ToString()) ||
                                              !string.IsNullOrWhiteSpace(itemKey.GetValue(null)?.ToString());
                            if (!hasCommand && !hasCaption)
                                continue;

                            bool hasLegacyDisable = itemKey.GetValue("LegacyDisable") != null;
                            string displayName = ResolveShellVerbDisplayName(itemKey, trimmedName);

                            string dedupeKey = $"{target.ScopeKey}|SHELL|{displayName}";
                            if (!seenKeys.Add(dedupeKey))
                                continue;

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
                                IsProtectedSystemItem = ProtectedSystemKeys.Contains(trimmedName)
                            });
                        }
                        else
                        {
                            string rawDefault = itemKey.GetValue(null)?.ToString()?.Trim() ?? string.Empty;
                            string rawClsid = rawDefault.Contains('{') ? rawDefault : (trimmedName.Contains('{') ? trimmedName : string.Empty);
                            if (string.IsNullOrEmpty(rawClsid)) continue;

                            bool prefixedDisabled = rawClsid.StartsWith('-');
                            string cleanClsid = prefixedDisabled ? rawClsid[1..].Trim() : rawClsid;
                            if (HiddenInternalClsids.Contains(cleanClsid))
                                continue;

                            // 验证对应的 ShellEx COM 组件 DLL 真实存在（过滤已卸载软件的死残留空壳）
                            if (!DoesClsidServerExist(cleanClsid))
                                continue;

                            bool blockedInPolicy = blockedClsids.Contains(cleanClsid);
                            string displayName = ResolveShellExDisplayName(trimmedName, cleanClsid);

                            string dedupeKey = $"{target.ScopeKey}|EX|{cleanClsid}";
                            if (!seenKeys.Add(dedupeKey))
                                continue;

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
                                IsProtectedSystemItem = ProtectedSystemKeys.Contains(trimmedName)
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
                .ThenBy(x => x.IsProtectedSystemItem ? 1 : 0) // 第三方软件排在前面，方便第一时间关掉！
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
                            // ignore read-only; unblock via HKCU Blocked below
                        }

                        SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, BlockedShellExPath, entry.Clsid);
                        entry.IsEnabled = true;
                        return new TweakResult(true, $"已恢复显示右键扩展「{entry.DisplayName}」。");
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

    private static bool DoesClsidServerExist(string clsid)
    {
        string? dllPath =
            SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\CLSID\{clsid}\InprocServer32", null)?.ToString()
            ?? SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, $@"Software\Classes\CLSID\{clsid}\InprocServer32", null)?.ToString()
            ?? SystemHelper.GetRegistryValue(RegistryHive.ClassesRoot, $@"CLSID\{clsid}\InprocServer32", null)?.ToString();

        if (string.IsNullOrWhiteSpace(dllPath))
            return false;

        string expanded = Environment.ExpandEnvironmentVariables(dllPath.Trim().Trim('"'));
        // 系统相对路径 DLL（如 shell32.dll）默认存在
        if (!expanded.Contains('\\'))
            return true;

        return File.Exists(expanded);
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
