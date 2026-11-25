using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 补充 5：网游防拉回 / 防跳 Ping — 关闭 TCP 接收段合并 (RSC) 并缩短初始重传超时 (InitialRTO: 1000ms -> 300ms)
/// </summary>
public sealed class OptimizeTcpRscAndRtoTweak : ITweakModule
{
    public string Id => "optimize_tcp_rsc_and_rto";
    public string Title => "网游防拉回：关闭 TCP 接收合并 (RSC) & 300ms 快速重传";
    public string Description => "关闭 2.5G/千兆网卡易引发瞬时跳 Ping 的接收段合并 (RSC)，并将丢包初始重传等待由默认 1000ms 缩短至 300ms，显著减少《永劫无间》网络波动时的人物拉回。";
    public string TechnicalDetail => "netsh int tcp set global rsc=disabled initialrto=300 nonsackrttresiliency=disabled";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE774";
    public bool NeedsExplorerRestart => false;

    public async Task<TweakStatus> CheckStatusAsync()
    {
        var (_, output, _) = await SystemHelper.RunPowerShellAsync(
            "$rto = (Get-NetTCPSetting -SettingName Internet -ErrorAction SilentlyContinue).InitialRto; " +
            "$rsc = (Get-NetOffloadGlobalSetting -ErrorAction SilentlyContinue).ReceiveSegmentCoalescing; " +
            "\"RTO=$rto;RSC=$rsc\"",
            timeoutSeconds: 12);

        bool rtoFast = output.Contains("RTO=300", StringComparison.OrdinalIgnoreCase);
        bool rscOff = output.Contains("RSC=Disabled", StringComparison.OrdinalIgnoreCase);

        if (rtoFast && rscOff)
            return new TweakStatus(true, "RSC 已关 / 300ms 重传");
        if (rtoFast || rscOff)
            return new TweakStatus(false, rscOff ? "仅关 RSC" : "仅开启 300ms 重传");

        return new TweakStatus(false, "系统默认 (RSC 开 / 1000ms)");
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        log("正在配置全局 TCP 参数：关闭接收段合并 (rsc=disabled) 并设置快速重传 (initialrto=300)...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "netsh int tcp set global rsc=disabled; netsh int tcp set global initialrto=300; netsh int tcp set global nonsackrttresiliency=disabled",
            log);

        if (code != 0)
            return new TweakResult(false, $"配置 TCP 全局参数失败：{err}");

        return new TweakResult(true, "已关闭 TCP 接收段合并 (RSC) 并将丢包重传超时提速至 300ms（已立即生效）。");
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        log("正在恢复 Windows 默认全局 TCP 参数 (rsc=enabled, initialrto=1000)...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "netsh int tcp set global rsc=enabled; netsh int tcp set global initialrto=1000",
            log);

        if (code != 0)
            return new TweakResult(false, $"恢复 TCP 全局参数失败：{err}");

        return new TweakResult(true, "已恢复系统默认 TCP 接收合并与 1000ms 重传设置。");
    }
}
