using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// Win11 安全静默免 UAC 弹窗 & 关闭 SmartScreen 筛选器。
/// 关键安全设计：强制保持 EnableLUA = 1（保护 AppContainer 沙箱、Adobe PR/UXP、WebView2 与微软商店绝不闪退），
/// 仅设置 ConsentPromptBehaviorAdmin = 0 与 PromptOnSecureDesktop = 0 实现无暗屏、无弹窗静默提权。
/// </summary>
public sealed class DisableUacAndSmartScreenTweak : ITweakModule
{
    private const string SystemPoliciesPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerMachinePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    private const string AppHostUserPath = @"Software\Microsoft\Windows\CurrentVersion\AppHost";

    public string Id => "disable_uac_and_smartscreen";
    public string Title => "静默关闭 UAC 弹窗 & SmartScreen（安全保沙箱版）";
    public string Description => "保持 EnableLUA=1 保护系统沙箱（防止 PR/Adobe UXP、微软商店闪退），仅关闭管理员弹窗与暗屏确认实现无感静默提权，并停用 SmartScreen 筛选。";
    public string TechnicalDetail => @"EnableLUA=1 (安全保留) + ConsentPromptBehaviorAdmin=0 + PromptOnSecureDesktop=0 + SmartScreen=Off";
    public string Category => "安全与权限";
    public string IconGlyph => "\uE72E";
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var lua = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA");
            var consent = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin");
            var secureDesktop = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "PromptOnSecureDesktop");
            var smart = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled")?.ToString();

            bool luaUnsafeOff = lua is int l0 && l0 == 0;
            if (luaUnsafeOff)
                return new TweakStatus(false, "不安全(EnableLUA=0，建议修复)");

            bool silentUac = (lua is null || (lua is int l1 && l1 == 1)) &&
                             (consent is int c && c == 0) &&
                             (secureDesktop is int s && s == 0);
            bool smartOff = string.Equals(smart, "Off", StringComparison.OrdinalIgnoreCase);

            if (silentUac && smartOff)
                return new TweakStatus(true, "安全静默无弹窗");
            if (silentUac || smartOff)
                return new TweakStatus(false, silentUac ? "仅静默 UAC" : "仅关 SmartScreen");

            return new TweakStatus(false, "系统默认（开启询问）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                bool wasLuaDisabled = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA") is int oldLua && oldLua == 0;

                log("正在确保 EnableLUA = 1（保护 Win11 沙箱与 Adobe PR / 微软商店兼容性）...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA", 1);

                log("正在开启管理员静默无弹窗提权 (ConsentPromptBehaviorAdmin=0, PromptOnSecureDesktop=0)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin", 0);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "PromptOnSecureDesktop", 0);

                log("正在关闭 Windows SmartScreen 应用与文件筛选器...");
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled", "Off");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AppHostUserPath, "EnableWebContentEvaluation", 0);

                string msg = wasLuaDisabled
                    ? "已将不安全的 EnableLUA=0 修复为安全静默无弹窗模式（EnableLUA=1）！请务必重启电脑以恢复系统沙箱与 PR 兼容性。"
                    : "已开启 Win11 安全静默无弹窗模式（不破坏系统沙箱）并关闭 SmartScreen。";

                return new TweakResult(true, msg, RecommendSystemReboot: wasLuaDisabled);
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
                bool wasLuaDisabled = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA") is int oldLua && oldLua == 0;

                log("正在恢复 Windows 默认 UAC 管理员确认弹窗 (EnableLUA=1, Consent=5) 与 SmartScreen 筛选...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA", 1);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin", 5);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "PromptOnSecureDesktop", 1);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled", "Warn");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AppHostUserPath, "EnableWebContentEvaluation", 1);

                return new TweakResult(
                    true,
                    wasLuaDisabled
                        ? "已恢复 EnableLUA=1 与默认 UAC 提示，请重启电脑使系统沙箱完全恢复生效！"
                        : "已恢复默认 UAC 弹窗提示与 SmartScreen 筛选器。",
                    RecommendSystemReboot: wasLuaDisabled);
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
