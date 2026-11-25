using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 2：关闭后台游戏自动录屏（GameDVR），同时确保「游戏模式 (Game Mode)」保持开启
/// </summary>
public sealed class OptimizeGameDvrAndModeTweak : ITweakModule
{
    private const string GameConfigStorePath = @"System\GameConfigStore";
    private const string CurrentUserGameDvrPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string PolicyGameDvrPath = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string GameBarPath = @"Software\Microsoft\GameBar";

    public string Id => "optimize_gamedvr_and_gamemode";
    public string Title => "关闭 GameDVR 后台录屏 + 锁定开启游戏模式";
    public string Description => "关闭 Xbox 后台静默视频捕获缓存消除偶发掉帧，同时强制保留 Windows 游戏模式开启（保障 Intel 大小核与锐龙 X3D 处理器调度正常）。";
    public string TechnicalDetail => @"AppCaptureEnabled=0, GameDVR_Enabled=0 + AutoGameModeEnabled=1";
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
            var gameMode = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, GameBarPath, "AutoGameModeEnabled");

            bool dvrOff = (dvrEnabled is int d && d == 0) &&
                          ((appCapture is int a && a == 0) || (policyDvr is int p && p == 0));
            bool modeOn = gameMode is null || (gameMode is int gm && gm == 1);

            if (dvrOff && modeOn)
                return new TweakStatus(true, "已关录屏 / 游戏模式开");

            return new TweakStatus(false, dvrOff ? "游戏模式未开" : "后台录屏未彻底关闭");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在禁用 GameDVR 后台画面捕获与组策略录制缓存...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameConfigStorePath, "GameDVR_Enabled", 0);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AppCaptureEnabled", 0);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PolicyGameDvrPath, "AllowGameDVR", 0);

                log("正在确认开启 Windows 游戏模式 (AutoGameModeEnabled = 1)...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "AllowAutoGameMode", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameBarPath, "AutoGameModeEnabled", 1);

                return new TweakResult(true, "已彻底关闭 GameDVR 后台录屏，并已确保 Windows 游戏模式处于开启状态。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"优化失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复系统默认的 GameDVR 录制功能...");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, GameConfigStorePath, "GameDVR_Enabled", 1);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, CurrentUserGameDvrPath, "AppCaptureEnabled", 1);
                SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, PolicyGameDvrPath, "AllowGameDVR");
                return new TweakResult(true, "已恢复系统默认 GameDVR 设置。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
