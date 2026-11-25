using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 隐藏 Win11 开始菜单下方的「全部」应用区域与推荐栏，仅保留上方干净的「已固定」图标
/// </summary>
public sealed class HideStartMenuAllAppsTweak : ITweakModule
{
    private const string PoliciesExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string WinPoliciesExplorerPath = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string PolicyMgrStartPath = @"SOFTWARE\Microsoft\PolicyManager\current\device\Start";

    public string Id => "hide_start_menu_all_apps";
    public string Title => "精简开始菜单：隐藏下方「全部」与推荐区域";
    public string Description => "启用开始菜单系统组策略 (NoStartMenuMorePrograms=1 & HideAppList=3 & HideRecommendedSection=1)，移除下方的「全部/类别」列表，仅保留干净的「已固定」图标区。";
    public string TechnicalDetail => @"Policies\Explorer (NoStartMenuMorePrograms=1) + PolicyManager\Start (HideAppList=3)";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uE8FC";
    public bool NeedsExplorerRestart => true;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var hkcuVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, PoliciesExplorerPath, "NoStartMenuMorePrograms");
            var cspVal = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideAppList");

            bool removed = (hkcuVal is int v1 && v1 == 1) || (cspVal is int v2 && v2 == 3);
            return removed
                ? new TweakStatus(true, "已隐藏下方列表")
                : new TweakStatus(false, "系统默认（显示全部列表）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在写入组策略：从开始菜单中移除「全部程序」列表 (NoStartMenuMorePrograms = 1, HideAppList = 3)...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, PoliciesExplorerPath, "NoStartMenuMorePrograms", 1);
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PoliciesExplorerPath, "NoStartMenuMorePrograms", 1);

            // 同步写入 Win11 24H2 PolicyManager CSP Start 策略与隐藏类别/推荐区域策略
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideAppList", 3);
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideCategoryView", 1);
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideRecommendedSection", 1);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, WinPoliciesExplorerPath, "HideCategoryView", 1);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, WinPoliciesExplorerPath, "HideRecommendedSection", 1);
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, WinPoliciesExplorerPath, "HideCategoryView", 1);
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, WinPoliciesExplorerPath, "HideRecommendedSection", 1);

            log("正在重启开始菜单宿主进程 (StartMenuExperienceHost.exe) 以重载布局...");
            await SystemHelper.RunPowerShellAsync(
                "Stop-Process -Name 'StartMenuExperienceHost' -Force -ErrorAction SilentlyContinue");

            return new TweakResult(
                true,
                "已写入隐藏开始菜单下方「全部」区域的组策略（建议点击重启资源管理器或重新登录使单页布局彻底刷新）。",
                RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置开始菜单策略失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            log("正在清除开始菜单隐藏策略，恢复默认的「全部」与类别区域...");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, PoliciesExplorerPath, "NoStartMenuMorePrograms");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PoliciesExplorerPath, "NoStartMenuMorePrograms");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideAppList");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideCategoryView");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PolicyMgrStartPath, "HideRecommendedSection");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, WinPoliciesExplorerPath, "HideCategoryView");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, WinPoliciesExplorerPath, "HideRecommendedSection");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, WinPoliciesExplorerPath, "HideCategoryView");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, WinPoliciesExplorerPath, "HideRecommendedSection");

            await SystemHelper.RunPowerShellAsync(
                "Stop-Process -Name 'StartMenuExperienceHost' -Force -ErrorAction SilentlyContinue");

            return new TweakResult(true, "已恢复开始菜单下方的「全部」应用列表显示。", RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复失败：{ex.Message}");
        }
    }
}
