using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 补充 3：文件资源管理器与任务栏清爽习惯合集
/// 复刻本机习惯：默认打开「此电脑」、显示后缀名与隐藏文件、隐藏任务栏搜索框/多任务按钮/Copilot、去除桌面「了解此图片」图标。
/// </summary>
public sealed class CleanExplorerAndTaskbarTweak : ITweakModule
{
    private const string ExplorerAdvPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string SearchPath = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string DesktopIconsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string SpotlightIconGuid = "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}";

    public string Id => "clean_explorer_and_taskbar_preset";
    public string Title => "资源管理器与任务栏清爽习惯合集";
    public string Description => "一键设置：打开文件管理器默认进「此电脑」、显示已知文件后缀与隐藏文件、隐藏任务栏搜索框/任务视图/Copilot 按钮、隐藏桌面「了解此图片」图标。";
    public string TechnicalDetail => @"Explorer\Advanced (LaunchTo=1, HideFileExt=0, ShowTaskViewButton=0) + SearchboxTaskbarMode=0";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uEC50"; // File Explorer icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var launchTo = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, ExplorerAdvPath, "LaunchTo");
            var hideExt = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, ExplorerAdvPath, "HideFileExt");
            var taskView = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, ExplorerAdvPath, "ShowTaskViewButton");
            var searchMode = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, SearchPath, "SearchboxTaskbarMode");

            bool ok = (launchTo is int lt && lt == 1) &&
                      (hideExt is int he && he == 0) &&
                      (taskView is int tv && tv == 0) &&
                      (searchMode is int sm && sm == 0);

            return ok
                ? new TweakStatus(true, "已应用清爽预设")
                : new TweakStatus(false, "未完全开启");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在设置文件资源管理器：默认打开「此电脑」、显示文件扩展名与隐藏文件...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "LaunchTo", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "HideFileExt", 0);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "Hidden", 1);

                log("正在精简任务栏：隐藏搜索框、任务视图按钮与 Copilot 图标...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, SearchPath, "SearchboxTaskbarMode", 0);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "ShowTaskViewButton", 0);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "ShowCopilotButton", 0);

                log("正在隐藏桌面聚焦「了解此图片」顽固图标...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, DesktopIconsPath, SpotlightIconGuid, 1);

                return new TweakResult(true, "已成功应用文件资源管理器与任务栏清爽习惯预设（大部分已即时生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"设置失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复 Windows 11 默认资源管理器与任务栏按钮设置...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "LaunchTo", 2);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "HideFileExt", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "Hidden", 2);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, SearchPath, "SearchboxTaskbarMode", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerAdvPath, "ShowTaskViewButton", 1);
                SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, DesktopIconsPath, SpotlightIconGuid);

                return new TweakResult(true, "已恢复系统默认资源管理器与任务栏显示。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
