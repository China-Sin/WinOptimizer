using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 3：多媒体类计划程序 (MMCSS) 游戏调度优先级拉满 + 解除网络发包节流限制
/// </summary>
public sealed class MultimediaGamingPriorityTweak : ITweakModule
{
    private const string SystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTaskPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public string Id => "mmcss_gaming_priority_and_net_unthrottle";
    public string Title => "拉满游戏线程调度优先级 & 解除网络节流";
    public string Description => "解除系统后台多媒体发包限流 (NetworkThrottlingIndex=FFFFFFFF)，降低后台预留占用，并将 Games 调度任务的 GPU 优先级 (8) 与 CPU 等级设为 High。";
    public string TechnicalDetail => @"HKLM\...\Multimedia\SystemProfile (NetworkThrottlingIndex=FFFFFFFF, SystemResponsiveness=0, Games GPU=8)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE945";
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var netThrottle = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemProfilePath, "NetworkThrottlingIndex");
            var sysResp = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemProfilePath, "SystemResponsiveness");
            var gpuPrio = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, GamesTaskPath, "GPU Priority");
            var cpuPrio = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, GamesTaskPath, "Priority");
            var schedCat = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, GamesTaskPath, "Scheduling Category")?.ToString();

            // DWORD 0xFFFFFFFF 在 .NET RegistryKey.GetValue 中被装箱为 int -1
            bool netUnthrottled = netThrottle is int nt && nt == unchecked((int)0xFFFFFFFF);
            bool lowResp = sysResp is int sr && sr <= 10;
            bool gamesHigh = (gpuPrio is int gp && gp == 8) &&
                             (cpuPrio is int cp && cp == 6) &&
                             string.Equals(schedCat, "High", StringComparison.OrdinalIgnoreCase);

            if (netUnthrottled && lowResp && gamesHigh)
                return new TweakStatus(true, "已拉满优先级 & 免节流");
            if (netUnthrottled && lowResp)
                return new TweakStatus(false, "已免节流（Games 未拉满）");

            return new TweakStatus(false, "系统默认调度");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在解除网络多媒体节流 (NetworkThrottlingIndex = 0xFFFFFFFF, SystemResponsiveness = 0)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemProfilePath, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemProfilePath, "SystemResponsiveness", 0);

                log("正在提升 MMCSS Games 任务的 GPU Priority (8) 与 CPU High 调度权重...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GamesTaskPath, "GPU Priority", 8);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GamesTaskPath, "Priority", 6);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, GamesTaskPath, "Scheduling Category", "High");
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, GamesTaskPath, "SFIO Priority", "High");

                return new TweakResult(true, "已解除系统网络节流并拉满游戏 GPU / CPU 多媒体线程调度权重。");
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
                log("正在恢复 Windows 默认 MMCSS 多媒体网络节流与调度参数...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemProfilePath, "NetworkThrottlingIndex", 10);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemProfilePath, "SystemResponsiveness", 20);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GamesTaskPath, "GPU Priority", 8);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, GamesTaskPath, "Priority", 2);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, GamesTaskPath, "Scheduling Category", "Medium");
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, GamesTaskPath, "SFIO Priority", "Normal");

                return new TweakResult(true, "已恢复默认多媒体调度与网络节流配置。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
