namespace WinOptimizer.Core;

public record TweakResult(
    bool Success,
    string Message,
    bool RecommendExplorerRestart = false
);

public record TweakStatus(
    bool IsOptimized,
    string StatusBadgeText
);

/// <summary>
/// 每一个系统优化功能的统一模块接口。
/// 后续新增第 6、7、8... 个功能时，只需新建一个类实现此接口并注册到 TweakRegistry 即可。
/// </summary>
public interface ITweakModule
{
    /// <summary>唯一标识符</summary>
    string Id { get; }

    /// <summary>功能标题</summary>
    string Title { get; }

    /// <summary>功能说明</summary>
    string Description { get; }

    /// <summary>底层实现原理简述（注册表路径或系统接口）</summary>
    string TechnicalDetail { get; }

    /// <summary>所属分类（如：安全与权限、桌面与美化、系统与更新）</summary>
    string Category { get; }

    /// <summary>Segoe Fluent / MDL2 图标字符</summary>
    string IconGlyph { get; }

    /// <summary>应用或还原后是否建议重启资源管理器生效</summary>
    bool NeedsExplorerRestart { get; }

    /// <summary>检测当前系统是否已处于优化开启状态</summary>
    Task<TweakStatus> CheckStatusAsync();

    /// <summary>执行优化（开启此项优化）</summary>
    Task<TweakResult> ApplyAsync(Action<string> log);

    /// <summary>恢复系统默认状态（撤销此项优化）</summary>
    Task<TweakResult> RevertAsync(Action<string> log);
}
