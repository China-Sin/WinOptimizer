using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 1：关闭内核隔离「内存完整性」(HVCI) 与基于虚拟化的安全性 (VBS)
/// 仅关闭 DeviceGuard / HVCI 内存完整性虚拟化开销，不破坏 WSL2 / Hyper-V 本身。
/// </summary>
public sealed class DisableVbsHvciTweak : ITweakModule
{
    private const string DeviceGuardPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string HvciPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    public string Id => "disable_vbs_hvci";
    public string Title => "关闭内核隔离「内存完整性」(HVCI / VBS)";
    public string Description => "消除虚拟化二级地址转换带来的 CPU 性能损耗，提升 CS2、永劫无间、PUBG 等网游 5%~15% 平均帧与 1% Low 帧（重启电脑后完全生效）。";
    public string TechnicalDetail => @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard (EnableVirtualizationBasedSecurity=0, HVCI Enabled=0)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7FC"; // Game controller icon
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var hvciObj = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, HvciPath, "Enabled");
            var vbsObj = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, DeviceGuardPath, "EnableVirtualizationBasedSecurity");

            bool hvciOn = hvciObj is int h && h == 1;
            bool vbsOn = vbsObj is int v && v == 1;

            if (!hvciOn && !vbsOn)
                return new TweakStatus(true, "已关闭（满帧状态）");
            if (hvciOn && vbsOn)
                return new TweakStatus(false, "已开启（影响帧率）");

            return new TweakStatus(false, hvciOn ? "内存完整性已开" : "VBS 已开启");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在关闭内核隔离「内存完整性」(HypervisorEnforcedCodeIntegrity = 0)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, HvciPath, "Enabled", 0);

                log("正在关闭基于虚拟化的安全性 (EnableVirtualizationBasedSecurity = 0)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, DeviceGuardPath, "EnableVirtualizationBasedSecurity", 0);

                return new TweakResult(true, "已关闭内存完整性 (HVCI) 与虚拟化安全 (VBS)，重启电脑后帧率提升完全生效。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"关闭 HVCI/VBS 失败（若系统开启了固件级锁请先在安全中心关闭内核隔离）：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复开启内存完整性 (HVCI) 与基于虚拟化的安全性...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, DeviceGuardPath, "EnableVirtualizationBasedSecurity", 1);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, HvciPath, "Enabled", 1);
                return new TweakResult(true, "已恢复开启内核隔离内存完整性（重启电脑后生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
