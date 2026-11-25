using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 10：精简后台遥测收集、系统广告推荐与开始菜单联网 Bing 搜索
/// </summary>
public sealed class DisableTelemetryAndBingSearchTweak : ITweakModule
{
    private const string DataCollectionPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string ExplorerPolicyPath = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string SearchPath = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string AdvertisingPath = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string ContentDeliveryPath = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static readonly string[] ContentSubKeys =
    [
        "SubscribedContent-338393Enabled",
        "SubscribedContent-353694Enabled",
        "SubscribedContent-353696Enabled",
        "SystemPaneSuggestionsEnabled",
        "SoftLandingEnabled"
    ];

    public string Id => "disable_telemetry_ads_bing_search";
    public string Title => "精简后台诊断遥测、系统广告与联网搜索";
    public string Description => "停用 DiagTrack 遥测收集服务、关闭设置与锁屏个性化广告推荐，并禁止开始菜单搜索本地软件时联网调起 Bing 网页卡顿。";
    public string TechnicalDetail => @"AllowTelemetry=0 + DisableSearchBoxSuggestions=1 + 停止 DiagTrack 服务";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uE71D";
    public bool NeedsExplorerRestart => true;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var telemetry = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, DataCollectionPolicyPath, "AllowTelemetry");
            var noBing = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, ExplorerPolicyPath, "DisableSearchBoxSuggestions");
            var adEnabled = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AdvertisingPath, "Enabled");

            bool telOff = telemetry is int t && t == 0;
            bool bingOff = noBing is int b && b == 1;
            bool adsOff = adEnabled is int a && a == 0;

            if (telOff && bingOff && adsOff)
                return new TweakStatus(true, "已精简遥测/广告/联网搜索");
            if (telOff || bingOff)
                return new TweakStatus(false, "部分已精简");

            return new TweakStatus(false, "系统默认（含遥测与推广）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在写入组策略禁止诊断遥测数据上传 (AllowTelemetry = 0)...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, DataCollectionPolicyPath, "AllowTelemetry", 0);

            log("正在关闭开始菜单联网 Bing 搜索建议，提速本地搜索响应...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ExplorerPolicyPath, "DisableSearchBoxSuggestions", 1);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, SearchPath, "BingSearchEnabled", 0);

            log("正在关闭系统广告 ID 与设置面板推广建议...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AdvertisingPath, "Enabled", 0);
            foreach (var key in ContentSubKeys)
            {
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, ContentDeliveryPath, key, 0);
            }

            log("正在停止并禁用后台诊断跟踪服务 (DiagTrack)...");
            await SystemHelper.RunPowerShellAsync(
                "Stop-Service -Name 'DiagTrack' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'DiagTrack' -StartupType Disabled -ErrorAction SilentlyContinue");

            return new TweakResult(true, "已关闭后台遥测、广告推送与开始菜单联网搜索。", RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"精简遥测与广告失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            log("正在恢复默认遥测策略、广告开关与开始菜单联网搜索...");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, DataCollectionPolicyPath, "AllowTelemetry");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, ExplorerPolicyPath, "DisableSearchBoxSuggestions");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, SearchPath, "BingSearchEnabled");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AdvertisingPath, "Enabled", 1);
            foreach (var key in ContentSubKeys)
            {
                SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, ContentDeliveryPath, key);
            }

            log("正在恢复 DiagTrack 诊断服务为自动启动...");
            await SystemHelper.RunPowerShellAsync(
                "Set-Service -Name 'DiagTrack' -StartupType Automatic -ErrorAction SilentlyContinue; Start-Service -Name 'DiagTrack' -ErrorAction SilentlyContinue");

            return new TweakResult(true, "已恢复默认遥测与搜索框联网功能。", RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复失败：{ex.Message}");
        }
    }
}
