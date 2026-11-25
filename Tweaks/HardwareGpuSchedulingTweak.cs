using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 4：硬件加速 GPU 计划 (HAGS - Hardware-Accelerated GPU Scheduling) 开关
/// RTX 40/50 系开启 DLSS 帧生成必开 (HwSchMode=2)；若遇到双屏直播副屏掉帧可一键关闭 (HwSchMode=1)。
/// </summary>
public sealed class HardwareGpuSchedulingTweak : ITweakModule
{
    private const string GraphicsDriversPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public string Id => "hardware_gpu_scheduling";
    public string Title => "开启「硬件加速 GPU 计划」(HAGS)";
    public string Description => "将显卡显存帧队列交由 GPU 独立调度以降低渲染延迟，RTX 40/50 系开启 DLSS 3/4 帧生成必开（重启生效；若双屏看视频掉帧可关此开关排障）。";
    public string TechnicalDetail => @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers (HwSchMode: 2=开启, 1=关闭)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7F4";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, GraphicsDriversPath, "HwSchMode");
            if (val is int mode)
            {
                return mode == 2
                    ? new TweakStatus(true, "已开启（支持帧生成）")
                    : new TweakStatus(false, "已关闭");
            }
            return new TweakStatus(false, "驱动默认");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在设置硬件加速 GPU 计划 HwSchMode = 2 (开启)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GraphicsDriversPath, "HwSchMode", 2);
                return new TweakResult(true, "已开启硬件加速 GPU 计划 (HAGS)，重启电脑后生效。");
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
                log("正在设置硬件加速 GPU 计划 HwSchMode = 1 (关闭)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GraphicsDriversPath, "HwSchMode", 1);
                return new TweakResult(true, "已关闭硬件加速 GPU 计划 (HAGS)，重启电脑后生效。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"关闭失败：{ex.Message}");
            }
        });
    }
}
