using System.IO;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 1：永久关闭“打开文件 - 安全警告”
/// 对应组策略：附件管理器 -> 文件附件中不保留区域信息 + 低风险文件类型的包含列表
/// </summary>
public sealed class DisableFileSecurityWarningTweak : ITweakModule
{
    private const string AttachmentsPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Attachments";
    private const string AssociationsPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations";
    private const string LowRiskExtensions = ".exe;.msi;.bat;.cmd;.reg;.ps1;.vbs;.js;.msc;.zip;.rar;.7z;.iso";

    public string Id => "disable_file_security_warning";
    public string Title => "关闭「打开文件 - 安全警告」弹窗";
    public string Description => "启用附件管理器组策略：下载文件不再打网络来源标记，并将常见安装包/脚本纳入低风险列表，同时自动解锁下载目录已有文件。";
    public string TechnicalDetail => @"组策略注册表 HKCU\...\Policies\Attachments (SaveZoneInformation=1) & Associations (LowRiskFileTypes)";
    public string Category => "安全与权限";
    public string IconGlyph => "\uE72E"; // Lock / Shield icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var saveZone = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AttachmentsPath, "SaveZoneInformation");
            var lowRisk = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AssociationsPath, "LowRiskFileTypes")?.ToString();

            bool zoneDisabled = saveZone is int v && v == 1;
            bool hasExeLowRisk = !string.IsNullOrWhiteSpace(lowRisk) &&
                                 lowRisk.Contains(".exe", StringComparison.OrdinalIgnoreCase);

            if (zoneDisabled && hasExeLowRisk)
                return new TweakStatus(true, "已关闭警告");
            if (zoneDisabled || hasExeLowRisk)
                return new TweakStatus(false, "部分开启");

            return new TweakStatus(false, "系统默认（询问）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在写入组策略「文件附件中不保留区域信息」(SaveZoneInformation = 1)...");
            SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AttachmentsPath, "SaveZoneInformation", 1);

            log($"正在配置组策略「低风险文件类型的包含列表」({LowRiskExtensions})...");
            SystemHelper.SetRegistryString(RegistryHive.CurrentUser, AssociationsPath, "LowRiskFileTypes", LowRiskExtensions);

            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                log($"正在批量清除下载目录 ({downloadsPath}) 现有文件的安全锁定标记...");
                var escapedPath = downloadsPath.Replace("'", "''");
                await SystemHelper.RunPowerShellAsync(
                    $"Get-ChildItem -LiteralPath '{escapedPath}' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue");
            }

            return new TweakResult(true, "已永久关闭「打开文件 - 安全警告」，并已解锁下载目录中的现有文件。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置失败：{ex.Message}");
        }
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在移除附件管理器自定义策略，恢复 Windows 默认安全询问...");
                SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, AttachmentsPath, "SaveZoneInformation");
                SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, AssociationsPath, "LowRiskFileTypes");
                return new TweakResult(true, "已恢复系统默认的「打开文件 - 安全警告」提示。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
