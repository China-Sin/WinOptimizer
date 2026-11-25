using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 11：SSD 游戏加载加速 — 禁用 NTFS 最后访问时间戳更新与 8.3 旧式短文件名生成
/// </summary>
public sealed class OptimizeSsdNtfsTweak : ITweakModule
{
    private const string FileSystemPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    public string Id => "optimize_ssd_ntfs_lastaccess";
    public string Title => "SSD 读写加速：禁用 NTFS 最后访问时间更新";
    public string Description => "关闭每次读取游戏材质/小文件时额外回写磁盘「最后访问时间戳」与 8.3 短文件名生成开销，降低 SSD 无效写入放大与 I/O 延迟。";
    public string TechnicalDetail => "fsutil behavior set disablelastaccess 1 & disable8dot3 1";
    public string Category => "性能与电源";
    public string IconGlyph => "\uEDA2";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, FileSystemPath, "NtfsDisableLastAccessUpdate");
            // 低位 bit 0 为 1（例如 1 或 0x80000001 / 0x80000003）代表已禁用最后访问时间更新
            bool lastAccessDisabled = val is int v && (v & 1) == 1;
            return lastAccessDisabled
                ? new TweakStatus(true, "已禁用时间戳回写")
                : new TweakStatus(false, "系统默认（开启回写）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        log("正在执行 fsutil 禁用 NTFS 最后访问时间戳更新与 8.3 短文件名创建...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "fsutil behavior set disablelastaccess 1; fsutil behavior set disable8dot3 1",
            log);

        if (code != 0)
            return new TweakResult(false, $"设置 NTFS 优化参数失败：{err}");

        return new TweakResult(true, "已禁用 NTFS 最后访问时间戳回写与 8.3 短文件名开销。");
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        log("正在恢复 Windows 11 系统默认 NTFS 访问时间策略 (System Managed = 2)...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync(
            "fsutil behavior set disablelastaccess 2; fsutil behavior set disable8dot3 2",
            log);

        if (code != 0)
            return new TweakResult(false, $"恢复 NTFS 默认设置失败：{err}");

        return new TweakResult(true, "已恢复系统默认 NTFS 文件系统行为。");
    }
}
