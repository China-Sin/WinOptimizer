using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 2：彻底关闭连按 5 次 Shift 粘滞键/筛选键弹窗钩子 + 高轮询率键鼠驱动队列优化
/// </summary>
public sealed partial class OptimizeInputAndStickyKeysTweak : ITweakModule
{
    private const string StickyKeysPath = @"Control Panel\Accessibility\StickyKeys";
    private const string FilterKeysPath = @"Control Panel\Accessibility\Keyboard Response";
    private const string ToggleKeysPath = @"Control Panel\Accessibility\ToggleKeys";
    private const string MouClassPath = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string KbdClassPath = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";

    private const uint SpiSetStickyKeys = 0x003B;
    private const uint SpiSetFilterKeys = 0x0033;
    private const uint SpiSetToggleKeys = 0x0035;
    private const uint SpifUpdateAndSend = 0x01 | 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct STICKYKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        ref STICKYKEYS pvParam,
        uint fWinIni);

    public string Id => "optimize_input_and_stickykeys";
    public string Title => "关闭连按 Shift 粘滞键弹窗 & 优化键鼠输入队列";
    public string Description => "彻底禁用连按 5 次 Shift 粘滞键/筛选键/切换键快捷键拦截（防止永劫高频闪避切出桌面暴毙），并优化高回报率键鼠驱动缓冲队列。";
    public string TechnicalDetail => @"StickyKeys Flags=506 (实时生效) + mouclass/kbdclass DataQueueSize=64";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE962";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var stickyFlags = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, StickyKeysPath, "Flags")?.ToString();
            var mouQueue = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, MouClassPath, "MouseDataQueueSize");

            // SKF_HOTKEYACTIVE = 0x00000004；当 bit 2 为 0（如 506 或 26）时，连按 5 次 Shift 快捷键已关闭
            bool hotkeyOff = int.TryParse(stickyFlags, out int sf) && (sf & 4) == 0;
            bool queueTuned = mouQueue is int mq && mq is >= 32 and <= 80;

            if (hotkeyOff && queueTuned)
                return new TweakStatus(true, "防弹窗 & 队列已优化");
            if (hotkeyOff)
                return new TweakStatus(false, "粘滞键已关（队列默认）");

            return new TweakStatus(false, "粘滞键未关（连按Shift易弹窗）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在禁用粘滞键、筛选键与切换键快捷键钩子，并通过 Win32 接口实时生效...");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, StickyKeysPath, "Flags", "506");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, FilterKeysPath, "Flags", "122");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, ToggleKeysPath, "Flags", "58");

                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>(), dwFlags = 506u };
                SystemParametersInfoW(SpiSetStickyKeys, sk.cbSize, ref sk, SpifUpdateAndSend);

                var tk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>(), dwFlags = 58u };
                SystemParametersInfoW(SpiSetToggleKeys, tk.cbSize, ref tk, SpifUpdateAndSend);

                log("正在调优高轮询率键鼠驱动队列深度 (MouseDataQueueSize=64, KeyboardDataQueueSize=64)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, MouClassPath, "MouseDataQueueSize", 64);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, KbdClassPath, "KeyboardDataQueueSize", 64);

                return new TweakResult(true, "已彻底关闭连按 Shift 粘滞键弹窗（已实时生效），并已完成键鼠驱动队列优化。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"优化键鼠与粘滞键失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复 Windows 默认辅助按键快捷键与键鼠驱动队列大小 (100)...");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, StickyKeysPath, "Flags", "510");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, FilterKeysPath, "Flags", "126");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, ToggleKeysPath, "Flags", "62");

                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>(), dwFlags = 510u };
                SystemParametersInfoW(SpiSetStickyKeys, sk.cbSize, ref sk, SpifUpdateAndSend);

                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, MouClassPath, "MouseDataQueueSize", 100);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, KbdClassPath, "KeyboardDataQueueSize", 100);

                return new TweakResult(true, "已恢复系统默认粘滞键与键鼠队列参数。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
