using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 彻底关闭 Xbox Game Bar 覆盖层、禁用组策略「Windows 游戏录制和广播」及后台广播服务，
/// 同时保留 Windows「游戏模式 (Game Mode)」开启以保障 Intel 大小核与锐龙 X3D 正常调度。
/// （不强行卸载 XboxGamingOverlay 包，避免触发系统“需要新应用打开 ms-gamebar 链接”弹窗报错）
/// </summary>
public sealed class OptimizeGameDvrAndModeTweak : ITweakModule
{
    private const string GameConfigStorePath = @"System\GameConfigStore";
    private const string CurrentUserGameDvrPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string PolicyGameDvrPath = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string GameBarPath = @"Software\Microsoft\GameBar";
    private const string BroadcastServicePath = @"SYSTEM\CurrentControlSet\Services\BcastDVRUserService";

    public string Id => "optimize_gamedvr_and_gamemode";
    public string Title => "关闭 Game Bar & 禁用 Windows 游戏录制和广播";
    public string Description => "关闭 Xbox Game Bar 覆盖层、手柄唤醒与启动提示，禁用组策略「Windows 游戏录制和广播」及后台广播服务 (BcastDVRUserService)，同时锁定保留「游戏模式」开启。";
    public string TechnicalDetail => @"AllowGameDVR=0, UseNexusForGameBarEnabled=0, ShowStartupPanel=0, BcastDVRUserService=Disabled";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7FC";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var dvrEnabled = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, GameConfigStorePath, "GameDVR_Enabled");
            var appCapture = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AppCaptureEnabled");
            var policyDvr = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PolicyGameDvrPath, "AllowGameDVR");
            var nexusBar = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled");
            var startupPanel = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, GameBarPath, "ShowStartupPanel");
            var bcastSvc = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, BroadcastServicePath, "Start");
            var gameMode = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, GameBarPath, "AutoGameModeEnabled");

            bool dvrPolicyOff = (dvrEnabled is int d && d == 0) &&
                                (appCapture is int a && a == 0) &&
                                (policyDvr is int p && p == 0);
            bool gameBarOff = (nexusBar is int n && n == 0) &&
                              (startupPanel is int s && s == 0) &&
                              (bcastSvc is int b && b == 4);
            bool modeOn = gameMode is null || (gameMode is int gm && gm == 1);

            if (dvrPolicyOff && gameBarOff && modeOn)
                return new TweakStatus(true, "已关 Game Bar 与录制广播");
            if (dvrPolicyOff)
                return new TweakStatus(false, "已关录屏（Game Bar 未彻底关闭）");

            return new TweakStatus(false, "系统默认（开启）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在关闭 Xbox Game Bar 覆盖层、控制器唤醒与游戏启动提示面板...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "ShowStartupPanel", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "GamePanelStartupTipIndex", 3);

            log("正在禁用组策略「Windows 游戏录制和广播」(AllowGameDVR = 0) 与全部后台音视频捕获...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PolicyGameDvrPath, "AllowGameDVR", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameConfigStorePath, "GameDVR_Enabled", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AppCaptureEnabled", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AudioCaptureEnabled", 0);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "HistoricalCaptureEnabled", 0);

            log("正在停止并禁用游戏录制与广播后台用户服务 (BcastDVRUserService)...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, BroadcastServicePath, "Start", 4);
            await SystemHelper.RunPowerShellAsync(
                "Stop-Process -Name 'GameBar','GameBarFTServer','GameBarPresenceWriter' -Force -ErrorAction SilentlyContinue; " +
                "Get-Service -Name 'BcastDVRUserService*' -ErrorAction SilentlyContinue | Stop-Service -Force -ErrorAction SilentlyContinue");

            log("正在确认保持 Windows 游戏模式开启 (AutoGameModeEnabled = 1)...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "AllowAutoGameMode", 1);
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "AutoGameModeEnabled", 1);

            return new TweakResult(true, "已彻底关闭 Game Bar、禁用 Windows 游戏录制与广播服务，并保持「游戏模式」开启。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"优化失败：{ex.Message}");
        }
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复 Xbox Game Bar 与 Windows 游戏录制/广播默认设置...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "ShowStartupPanel", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameConfigStorePath, "GameDVR_Enabled", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AppCaptureEnabled", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AudioCaptureEnabled", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "HistoricalCaptureEnabled", 1);
                SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PolicyGameDvrPath, "AllowGameDVR");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, BroadcastServicePath, "Start", 3); // 恢复手动启动

                return new TweakResult(true, "已恢复系统默认 Game Bar 与游戏录制广播功能。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
