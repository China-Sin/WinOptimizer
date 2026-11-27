namespace WinOptimizer.Core;

public enum TweakRestartRequirement
{
    /// <summary>开启或关闭后即时生效（无需重启）</summary>
    None,

    /// <summary>开启或关闭后需重启 Windows 资源管理器生效</summary>
    ExplorerRestart,

    /// <summary>开启或关闭后需重启电脑才能完全生效</summary>
    SystemReboot
}

public record TweakResult(
    bool Success,
    string Message,
    bool RecommendExplorerRestart = false,
    bool RecommendSystemReboot = false
);

public record TweakStatus(
    bool IsOptimized,
    string StatusBadgeText
);

/// <summary>
/// 每一个系统优化功能的统一模块接口。
/// 后续新增功能时，只需实现此接口并注册到 TweakRegistry 即可。
/// </summary>
public interface ITweakModule
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    string TechnicalDetail { get; }
    string Category { get; }
    string IconGlyph { get; }

    /// <summary>应用或还原后是否建议重启资源管理器生效</summary>
    bool NeedsExplorerRestart { get; }

    /// <summary>明确标注此项开启或关闭后的生效方式（即时生效 / 需重启资源管理器 / 需重启电脑）</summary>
    TweakRestartRequirement RestartRequirement =>
        NeedsExplorerRestart ? TweakRestartRequirement.ExplorerRestart : TweakRestartRequirement.None;

    Task<TweakStatus> CheckStatusAsync();
    Task<TweakResult> ApplyAsync(Action<string> log);
    Task<TweakResult> RevertAsync(Action<string> log);
}
