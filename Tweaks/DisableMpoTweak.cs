using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 7：关闭桌面窗口管理器多平面叠加 (MPO - Multiplane Overlay)
/// 修复 Win11 高刷/双屏切出游戏黑屏 2 秒、浏览器视频闪屏及无边框游戏微卡顿。
/// </summary>
public sealed class DisableMpoTweak : ITweakModule
{
    private const string DwmPath = @"SOFTWARE\Microsoft\Windows\Dwm";

    public string Id => "disable_dwm_mpo";
    public string Title => "关闭多平面叠加 (MPO) 防黑屏与闪屏";
    public string Description => "写入官方 OverlayTestMode=5 禁用 DWM 多平面叠加，解决高刷显示器 Alt+Tab 切屏黑屏、双屏看视频掉帧及部分浏览器硬件加速局部花屏问题。";
    public string TechnicalDetail => @"HKLM\SOFTWARE\Microsoft\Windows\Dwm (OverlayTestMode=5)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7F4";
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, DwmPath, "OverlayTestMode");
            return (val is int v && v == 5)
                ? new TweakStatus(true, "已关闭 MPO")
                : new TweakStatus(false, "系统默认（MPO 开启）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在设置 HKLM\\SOFTWARE\\Microsoft\\Windows\\Dwm\\OverlayTestMode = 5...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, DwmPath, "OverlayTestMode", 5);
                return new TweakResult(true, "已关闭多平面叠加 (MPO)，重启电脑后生效。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"关闭 MPO 失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在移除 OverlayTestMode，恢复系统默认 MPO 多平面叠加...");
                SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, DwmPath, "OverlayTestMode");
                return new TweakResult(true, "已恢复系统默认 MPO 设置（重启电脑后生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
