using System.IO;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 9：关闭系统休眠 (powercfg /h off) 清理 hiberfil.sys 释放数十 GB C 盘空间
/// </summary>
public sealed class DisableHibernationTweak : ITweakModule
{
    private const string PowerControlPath = @"SYSTEM\CurrentControlSet\Control\Power";
    private const string HiberFilePath = @"C:\hiberfil.sys";

    public string Id => "disable_system_hibernation";
    public string Title => "关闭系统休眠（清理 hiberfil.sys 释放 C 盘空间）";
    public string Description => "关闭台式机鲜少使用的休眠与快速启动假关机机制，立即删除 C:\\hiberfil.sys 休眠镜像，瞬间腾出 10GB~30GB 系统盘空间。";
    public string TechnicalDetail => "powercfg /hibernate off (HibernateEnabled=0)";
    public string Category => "性能与电源";
    public string IconGlyph => "\uEDA2"; // Hard drive / storage icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerControlPath, "HibernateEnabled");
            bool regDisabled = val is int v && v == 0;
            bool fileExists = File.Exists(HiberFilePath);

            if (regDisabled && !fileExists)
                return new TweakStatus(true, "已关闭（空间已释放）");

            if (fileExists)
            {
                try
                {
                    double gb = new FileInfo(HiberFilePath).Length / 1073741824.0;
                    return new TweakStatus(false, $"已开启 (占用 {gb:F1} GB)");
                }
                catch
                {
                    return new TweakStatus(false, "已开启（占用 C 盘）");
                }
            }

            return new TweakStatus(false, "系统默认（开启）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        log("正在执行 powercfg /hibernate off 关闭休眠并删除 C:\\hiberfil.sys...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync("powercfg /hibernate off", log);
        if (code != 0)
            return new TweakResult(false, $"关闭休眠失败：{err}");

        return new TweakResult(true, "已成功关闭系统休眠并释放 C 盘休眠文件占用空间。");
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        log("正在执行 powercfg /hibernate on 重新开启系统休眠功能...");
        var (code, _, err) = await SystemHelper.RunPowerShellAsync("powercfg /hibernate on", log);
        if (code != 0)
            return new TweakResult(false, $"开启休眠失败：{err}");

        return new TweakResult(true, "已恢复开启系统休眠与快速启动功能。");
    }
}
