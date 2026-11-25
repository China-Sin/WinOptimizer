using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 4：修改右键菜单为 Win10 经典完整样式
/// </summary>
public sealed class ClassicContextMenuTweak : ITweakModule
{
    private const string ClsidRootPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string InprocPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    public string Id => "win10_classic_context_menu";
    public string Title => "切换右键菜单为 Win10 经典样式";
    public string Description => "跳过 Win11 折叠右键菜单（无需再点“显示更多选项”），直接展示完整经典右键菜单。修改后重启资源管理器即刻生效。";
    public string TechnicalDetail => @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 (默认值置空)";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uE8FD"; // Context menu / list icon
    public bool NeedsExplorerRestart => true;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            if (!SystemHelper.SubKeyExists(RegistryHive.CurrentUser, InprocPath))
                return new TweakStatus(false, "Win11 折叠样式（默认）");

            var val = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, InprocPath, null);
            bool isEmptyDefault = val is string s && s.Length == 0;
            return isEmptyDefault
                ? new TweakStatus(true, "Win10 经典菜单")
                : new TweakStatus(false, "Win11 折叠样式（默认）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log($"正在写入注册表项 HKCU\\{InprocPath}...");
                SystemHelper.SetRegistryString(RegistryHive.CurrentUser, InprocPath, null, string.Empty);
                return new TweakResult(true, "已切换为 Win10 经典右键菜单。", RecommendExplorerRestart: true);
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"切换右键菜单失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log($"正在删除注册表项 HKCU\\{ClsidRootPath}，恢复 Win11 新版右键菜单...");
                SystemHelper.DeleteRegistrySubKeyTree(RegistryHive.CurrentUser, ClsidRootPath);
                return new TweakResult(true, "已恢复为 Win11 默认折叠右键菜单。", RecommendExplorerRestart: true);
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复右键菜单失败：{ex.Message}");
            }
        });
    }
}
