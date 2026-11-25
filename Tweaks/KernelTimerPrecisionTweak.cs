using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 3：开启 Win11 全局高精度时钟请求 (GlobalTimerResolutionRequests=1) + 禁用动态时钟节拍 + 清除错误 HPET
/// </summary>
public sealed class KernelTimerPrecisionTweak : ITweakModule
{
    private const string KernelPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

    public string Id => "win11_global_timer_precision";
    public string Title => "开启 Win11 全局高精度时钟 & 禁用动态时钟节拍";
    public string Description => "开启 GlobalTimerResolutionRequests=1 恢复全局 0.5ms 高精度时钟同步，同时禁用动态时钟中断停顿并清除误开的低效 HPET，大幅平滑 0.1% Low 帧毛刺。";
    public string TechnicalDetail => @"GlobalTimerResolutionRequests=1 + bcdedit disabledynamictick yes & delete useplatformclock";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE916"; // Stopwatch / Timer icon
    public bool NeedsExplorerRestart => false;

    public async Task<TweakStatus> CheckStatusAsync()
    {
        var globalTimer = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, KernelPath, "GlobalTimerResolutionRequests");
        bool globalOn = globalTimer is int g && g == 1;

        var (_, bcdOut, _) = await SystemHelper.RunPowerShellAsync("bcdedit /enum '{current}'", timeoutSeconds: 10);
        bool dynTickOff = bcdOut.Contains("disabledynamictick", StringComparison.OrdinalIgnoreCase) &&
                          (bcdOut.Contains("Yes", StringComparison.OrdinalIgnoreCase) || bcdOut.Contains("是"));
        bool badHpetOn = bcdOut.Contains("useplatformclock", StringComparison.OrdinalIgnoreCase) &&
                         (bcdOut.Contains("Yes", StringComparison.OrdinalIgnoreCase) || bcdOut.Contains("是"));

        if (globalOn && dynTickOff && !badHpetOn)
            return new TweakStatus(true, "全局高精度时钟已开启");
        if (badHpetOn)
            return new TweakStatus(false, "警告：误开了强制 HPET");

        return new TweakStatus(false, globalOn ? "仅全局时钟开启" : "Win11 默认隔离时钟");
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在写入 GlobalTimerResolutionRequests = 1（恢复全局高精度时钟同步）...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, KernelPath, "GlobalTimerResolutionRequests", 1);

            log("正在配置启动项：禁用动态时钟节拍 (disabledynamictick yes) 并确保清除低效强制 HPET...");
            await SystemHelper.RunPowerShellAsync(
                "bcdedit /set disabledynamictick yes; bcdedit /deletevalue useplatformclock 2>$null",
                log);

            return new TweakResult(true, "已开启 Win11 全局高精度时钟并校准硬件时钟参数（重启电脑后完全生效）。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置内核时钟失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            log("正在移除 GlobalTimerResolutionRequests 并恢复系统默认动态时钟节拍...");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, KernelPath, "GlobalTimerResolutionRequests");
            await SystemHelper.RunPowerShellAsync("bcdedit /deletevalue disabledynamictick 2>$null", log);
            return new TweakResult(true, "已恢复 Windows 11 默认内核时钟策略（重启电脑生效）。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复失败：{ex.Message}");
        }
    }
}
