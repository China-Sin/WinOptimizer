using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 3：彻底卸载并禁用 Win11 小组件（Windows Web Experience Pack）
/// 采用离线 Appx 秒卸载 + 组策略禁用后台进程与防止更新自动重装。
/// </summary>
public sealed class UninstallWidgetsTweak : ITweakModule
{
    private const string DshPolicyPath = @"SOFTWARE\Policies\Microsoft\Dsh";

    public string Id => "uninstall_widgets";
    public string Title => "卸载并禁用 Win11 小组件（Widgets）";
    public string Description => "离线秒卸 Windows Web Experience Pack 小组件包，结束后台 Widgets 进程，并写入组策略防止系统更新自动装回。";
    public string TechnicalDetail => @"Remove-AppxPackage *WebExperience* + HKLM\SOFTWARE\Policies\Microsoft\Dsh (AllowNewsAndInterests=0)";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uF246"; // Widgets / Dashboard icon
    public bool NeedsExplorerRestart => false;

    public async Task<TweakStatus> CheckStatusAsync()
    {
        var policyVal = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, DshPolicyPath, "AllowNewsAndInterests");
        bool policyDisabled = policyVal is int v && v == 0;

        var (_, output, _) = await SystemHelper.RunPowerShellAsync(
            "if (Get-AppxPackage *WebExperience* -ErrorAction SilentlyContinue) { 'INSTALLED' } else { 'REMOVED' }",
            timeoutSeconds: 15);

        bool packageRemoved = output.Contains("REMOVED", StringComparison.OrdinalIgnoreCase);

        if (packageRemoved && policyDisabled)
            return new TweakStatus(true, "已彻底卸载并禁用");
        if (packageRemoved)
            return new TweakStatus(true, "已卸载（未加防重装策略）");
        if (policyDisabled)
            return new TweakStatus(false, "仅策略禁用（包未卸）");

        return new TweakStatus(false, "已安装（默认）");
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在结束后台小组件相关进程 (Widgets.exe / WidgetService.exe)...");
            await SystemHelper.RunPowerShellAsync(
                "Stop-Process -Name 'Widgets','WidgetService' -Force -ErrorAction SilentlyContinue");

            log("正在写入组策略禁用小组件服务与任务栏入口 (AllowNewsAndInterests = 0)...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, DshPolicyPath, "AllowNewsAndInterests", 0);

            log("正在离线卸载 Windows Web Experience Pack 预装包与当前用户包...");
            var script = """
                Get-AppxPackage -AllUsers *WebExperience* -ErrorAction SilentlyContinue | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
                Get-AppxPackage *WebExperience* -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
                Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object DisplayName -like '*WebExperience*' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
                if (Get-AppxPackage *WebExperience* -ErrorAction SilentlyContinue) {
                    if (Get-Command winget -ErrorAction SilentlyContinue) {
                        winget uninstall --id 9MSSGKG348SP --silent --accept-source-agreements
                    }
                }
                """;
            await SystemHelper.RunPowerShellAsync(script, log, timeoutSeconds: 120);

            return new TweakResult(true, "已彻底卸载小组件并写入防自动重装策略。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"卸载小组件失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            log("正在移除小组件禁用组策略...");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, DshPolicyPath, "AllowNewsAndInterests");

            log("正在通过 winget 从微软商店源重新安装 Windows Web Experience Pack（需联网，请稍候）...");
            var (code, _, err) = await SystemHelper.RunPowerShellAsync(
                "winget install --id 9MSSGKG348SP --silent --accept-source-agreements --accept-package-agreements",
                log,
                timeoutSeconds: 180);

            if (code != 0)
            {
                return new TweakResult(true, $"已解除小组件禁用策略；自动下载安装包返回代码 {code}（你也可以在 Microsoft Store 搜索 Windows Web Experience Pack 安装）。{err}");
            }

            return new TweakResult(true, "已成功恢复并重新安装 Win11 小组件。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复小组件失败：{ex.Message}");
        }
    }
}
