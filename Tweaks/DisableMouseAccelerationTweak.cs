using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 5：彻底关闭鼠标加速（开启 1:1 线性原始输入曲线）
/// 同时通过 Win32 SystemParametersInfoW 即刻通知内核生效，无需注销或重启电脑！
/// </summary>
public sealed partial class DisableMouseAccelerationTweak : ITweakModule
{
    private const string MouseRegPath = @"Control Panel\Mouse";
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniAndBroadcast = 0x01 | 0x02;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        int[] pvParam,
        uint fWinIni);

    public string Id => "disable_mouse_acceleration";
    public string Title => "彻底关闭鼠标加速（1:1 原始输入）";
    public string Description => "关闭控制面板隐藏的「提高指针精确度」非线性加速算法，实现物理移动与准星 1:1 绝对线性对应，点击后免重启立即生效。";
    public string TechnicalDetail => @"HKCU\Control Panel\Mouse (MouseSpeed=0, Threshold1/2=0) + SystemParametersInfo(SPI_SETMOUSE)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE962"; // Mouse / Input icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var speed = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, MouseRegPath, "MouseSpeed")?.ToString();
            var t1 = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold1")?.ToString();
            var t2 = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold2")?.ToString();

            bool rawInput = speed == "0" && t1 == "0" && t2 == "0";
            return rawInput
                ? new TweakStatus(true, "1:1 无加速（已开启）")
                : new TweakStatus(false, "系统默认（含指针加速）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在将鼠标曲线阈值清零并调用 Win32 SPI_SETMOUSE 实时生效...");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseSpeed", "0");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold1", "0");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold2", "0");

                // pvParam 顺序: [MouseThreshold1, MouseThreshold2, MouseSpeed]
                SystemParametersInfoW(SpiSetMouse, 0, [0, 0, 0], SpifUpdateIniAndBroadcast);

                return new TweakResult(true, "已彻底关闭鼠标指针加速（当前已实时生效，无需重启）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"关闭鼠标加速失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复 Windows 默认指针精确度参数 (6 / 10 / 1)...");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseSpeed", "1");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold1", "6");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, MouseRegPath, "MouseThreshold2", "10");

                SystemParametersInfoW(SpiSetMouse, 0, [6, 10, 1], SpifUpdateIniAndBroadcast);

                return new TweakResult(true, "已恢复 Windows 默认鼠标指针加速设置（已实时生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
