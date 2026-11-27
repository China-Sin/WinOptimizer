using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 补充 4：调优系统服务宿主合并阈值 (SvcHostSplitThresholdInKB)
/// 减少任务管理器中拆分的零碎 svchost.exe 进程数量与内核句柄开销。
/// </summary>
public sealed partial class MergeSvcHostProcessesTweak : ITweakModule
{
    private const string ControlKeyPath = @"SYSTEM\CurrentControlSet\Control";
    private const int DefaultSplitThresholdKb = 3670016; // Windows 默认 3.5 GB
    private const int MinTargetThresholdKb = 67108864;   // 64 GB (67,108,864 KB)

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    public string Id => "merge_svchost_processes";
    public string Title => "合并系统服务宿主进程 (svchost.exe)";
    public string Description => "调高 SvcHostSplitThresholdInKB 进程拆分阈值，避免大内存电脑拆分出上百个零碎的 svchost.exe 后台进程，减少内存碎片与句柄占用。";
    public string TechnicalDetail => @"HKLM\SYSTEM\CurrentControlSet\Control (SvcHostSplitThresholdInKB = 67108864)";
    public string Category => "性能与电源";
    public string IconGlyph => "\uE950";
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            ulong ramKb = GetInstalledRamKb();
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, ControlKeyPath, "SvcHostSplitThresholdInKB");
            if (val is int threshold && (ulong)threshold >= ramKb)
            {
                return new TweakStatus(true, $"已合并 ({threshold / 1048576} GB 阈值)");
            }

            return new TweakStatus(false, "系统默认（独立拆分）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                ulong ramKb = GetInstalledRamKb();
                int targetKb = (int)Math.Min(int.MaxValue, Math.Max((ulong)MinTargetThresholdKb, ramKb + 4194304UL));

                log($"正在设置 SvcHostSplitThresholdInKB = {targetKb} KB...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, ControlKeyPath, "SvcHostSplitThresholdInKB", targetKb);
                return new TweakResult(true, "已优化 svchost.exe 服务宿主合并阈值（重启电脑后生效）。");
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
                log($"正在恢复 Windows 默认 SvcHostSplitThresholdInKB = {DefaultSplitThresholdKb} KB...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, ControlKeyPath, "SvcHostSplitThresholdInKB", DefaultSplitThresholdKb);
                return new TweakResult(true, "已恢复系统默认的 svchost.exe 独立进程拆分阈值（重启生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }

    private static ulong GetInstalledRamKb()
        => GetPhysicallyInstalledSystemMemory(out ulong kb) && kb > 0 ? kb : 33554432UL;
}
