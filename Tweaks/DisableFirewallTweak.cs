using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 2：关闭 Windows 防火墙（域 / 专用 / 公用配置文件）
/// 保留底层 mpssvc 服务运行，防止 Microsoft Store / Xbox / 局域网共享损坏。
/// </summary>
public sealed class DisableFirewallTweak : ITweakModule
{
    private static readonly string[] ProfileSubKeys =
    [
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile",
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile",
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile"
    ];

    public string Id => "disable_windows_firewall";
    public string Title => "关闭 Windows Defender 防火墙";
    public string Description => "一键关闭域网络、专用网络、公用网络防火墙拦截，同时保留底层防火墙服务存活，避免微软商店无法下载更新与共享报错。";
    public string TechnicalDetail => "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False";
    public string Category => "安全与权限";
    public string IconGlyph => "\uEA18"; // Shield / Firewall icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            int disabledCount = 0;
            foreach (var key in ProfileSubKeys)
            {
                var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, key, "EnableFirewall");
                if (val is int v && v == 0)
                    disabledCount++;
            }

            if (disabledCount == ProfileSubKeys.Length)
                return new TweakStatus(true, "已全部关闭");
            if (disabledCount > 0)
                return new TweakStatus(false, $"已关闭 {disabledCount}/3 个网络");

            return new TweakStatus(false, "已开启（默认）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        log("正在关闭域、专用、公用网络配置文件的 Windows 防火墙...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False -ErrorAction Stop",
            log);

        if (code != 0)
        {
            log("PowerShell 接口返回异常，正在使用 netsh advfirewall 兜底执行...");
            var fallback = await SystemHelper.RunPowerShellAsync("netsh advfirewall set allprofiles state off", log);
            if (fallback.ExitCode != 0)
                return new TweakResult(false, $"关闭防火墙失败：{err} {fallback.Error}".Trim());
        }

        return new TweakResult(true, "已成功关闭所有网络配置文件的 Windows 防火墙。");
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        log("正在重新开启域、专用、公用网络配置文件的 Windows 防火墙...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True -ErrorAction Stop",
            log);

        if (code != 0)
        {
            var fallback = await SystemHelper.RunPowerShellAsync("netsh advfirewall set allprofiles state on", log);
            if (fallback.ExitCode != 0)
                return new TweakResult(false, $"开启防火墙失败：{err} {fallback.Error}".Trim());
        }

        return new TweakResult(true, "已恢复开启所有网络配置文件的 Windows 防火墙。");
    }
}
