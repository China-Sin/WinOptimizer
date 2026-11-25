using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 补充 1：彻底关闭 UAC 管理员权限确认弹窗 & Windows SmartScreen 筛选器
/// </summary>
public sealed class DisableUacAndSmartScreenTweak : ITweakModule
{
    private const string SystemPoliciesPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerMachinePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    private const string AppHostUserPath = @"Software\Microsoft\Windows\CurrentVersion\AppHost";

    public string Id => "disable_uac_and_smartscreen";
    public string Title => "彻底关闭 UAC 权限确认弹窗 & SmartScreen 筛选器";
    public string Description => "关闭运行软件时的屏幕变暗 UAC 管理员弹窗确认，并停用 Explorer / 应用的 SmartScreen 联网筛选拦截，实现真正的零弹窗无感运行。";
    public string TechnicalDetail => @"Policies\System (EnableLUA=0, ConsentPromptBehaviorAdmin=0) + SmartScreenEnabled=Off";
    public string Category => "安全与权限";
    public string IconGlyph => "\uE72E";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var lua = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA");
            var consent = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin");
            var smart = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled")?.ToString();

            bool uacOff = (lua is int l && l == 0) && (consent is int c && c == 0);
            bool smartOff = string.Equals(smart, "Off", StringComparison.OrdinalIgnoreCase);

            if (uacOff && smartOff)
                return new TweakStatus(true, "已关闭 UAC 与筛选器");
            if (uacOff || smartOff)
                return new TweakStatus(false, uacOff ? "仅关 UAC" : "仅关 SmartScreen");

            return new TweakStatus(false, "系统默认（开启询问）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在关闭 UAC 用户账户控制弹窗与安全桌面暗屏切换...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin", 0);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "PromptOnSecureDesktop", 0);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA", 0);

                log("正在关闭 Windows SmartScreen 应用与文件筛选器...");
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled", "Off");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AppHostUserPath, "EnableWebContentEvaluation", 0);

                return new TweakResult(true, "已彻底关闭 UAC 权限确认弹窗与 SmartScreen 筛选器（EnableLUA 修改在重启电脑后完全生效）。");
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
                log("正在恢复 Windows 默认 UAC 管理员确认级别与 SmartScreen 筛选...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "EnableLUA", 1);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "ConsentPromptBehaviorAdmin", 5);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, SystemPoliciesPath, "PromptOnSecureDesktop", 1);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ExplorerMachinePath, "SmartScreenEnabled", "Warn");
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AppHostUserPath, "EnableWebContentEvaluation", 1);

                return new TweakResult(true, "已恢复默认 UAC 提示与 SmartScreen 筛选器（重启电脑生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
