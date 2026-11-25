using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 优化模块注册中心（共 29 项系统定制与电竞级优化）。
/// 后续新增任何功能，只需在此列表中追加一行 new YourNewTweak()，界面与批量执行逻辑将自动生成！
/// </summary>
public static class TweakRegistry
{
    public static IReadOnlyList<ITweakModule> CreateAllModules() =>
    [
        // 游戏与延迟
        new DisableNagleTcpLowLatencyTweak(),
        new OptimizeTcpRscAndRtoTweak(),
        new OptimizeInputAndStickyKeysTweak(),
        new KernelTimerPrecisionTweak(),
        new HeterogeneousPCoreSchedulingTweak(),
        new ForegroundAndNarakaPriorityTweak(),
        new NvidiaUnlimitedShaderCacheTweak(),
        new DisableMouseAccelerationTweak(),
        new DisableVbsHvciTweak(),
        new OptimizeGameDvrAndModeTweak(),
        new MultimediaGamingPriorityTweak(),
        new HardwareGpuSchedulingTweak(),
        new DisableMpoTweak(),
        new WindowedGameOptimizationTweak(),

        // 性能与电源
        new UltimatePerformancePowerTweak(),
        new MemoryResidentAndStandbyPurgeTweak(),
        new MergeSvcHostProcessesTweak(),
        new DisableHibernationTweak(),
        new OptimizeSsdNtfsTweak(),

        // 安全与权限
        new DisableFileSecurityWarningTweak(),
        new DisableUacAndSmartScreenTweak(),
        new DisableFirewallTweak(),

        // 桌面与体验
        new CleanExplorerAndTaskbarTweak(),
        new RemoveShortcutSuffixAndShieldTweak(),
        new ClassicContextMenuTweak(),
        new HideStartMenuAllAppsTweak(),
        new UninstallWidgetsTweak(),
        new DisableTelemetryAndBingSearchTweak(),

        // 系统与更新
        new PauseWindowsUpdateTweak()
    ];
}
