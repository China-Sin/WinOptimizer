using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 建议 8：开启 Windows 11「窗口化游戏优化」(DXGI Flip Model 翻转模型升级)
/// 让 DX10 / DX11 无边框窗口模式游戏获得与独占全屏一致的超低输入延迟与 AutoHDR/VRR 支持。
/// </summary>
public sealed class WindowedGameOptimizationTweak : ITweakModule
{
    private const string DirectXPrefsPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ValueName = "DirectXUserGlobalSettings";
    private const string TokenKey = "SwapEffectUpgradeEnable";

    public string Id => "enable_windowed_game_optimization";
    public string Title => "开启「窗口化游戏优化」(DXGI 低延迟翻转)";
    public string Description => "自动将旧版 DX10/DX11 无边框窗口游戏升级为现代独立翻转模型 (Flip Model)，大幅降低窗口化与无边框全屏的渲染输入延迟。";
    public string TechnicalDetail => @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences (SwapEffectUpgradeEnable=1;)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7FC";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var raw = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, DirectXPrefsPath, ValueName)?.ToString() ?? string.Empty;
            bool enabled = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                              .Any(part => part.Trim().Equals($"{TokenKey}=1", StringComparison.OrdinalIgnoreCase));

            return enabled
                ? new TweakStatus(true, "已开启（低延迟翻转）")
                : new TweakStatus(false, "未开启");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在配置 DirectX 全局设置 SwapEffectUpgradeEnable=1;...");
                UpdateDirectXToken(enable: true);
                return new TweakResult(true, "已开启 Windows 11「窗口化游戏优化」(下次启动游戏即生效)。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"开启失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在将 DirectX 全局设置恢复为 SwapEffectUpgradeEnable=0;...");
                UpdateDirectXToken(enable: false);
                return new TweakResult(true, "已关闭窗口化游戏优化。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"关闭失败：{ex.Message}");
            }
        });
    }

    private static void UpdateDirectXToken(bool enable)
    {
        var raw = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, DirectXPrefsPath, ValueName)?.ToString() ?? string.Empty;
        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Trim())
                       .Where(p => !p.StartsWith($"{TokenKey}=", StringComparison.OrdinalIgnoreCase))
                       .ToList();

        parts.Add($"{TokenKey}={(enable ? "1" : "0")}");
        string updated = string.Join(";", parts) + ";";
        SystemHelper.SetRegistryString(RegistryHive.CurrentUser, DirectXPrefsPath, ValueName, updated);
    }
}
