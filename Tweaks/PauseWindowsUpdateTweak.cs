using System.Globalization;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 5：一键将 Windows 系统自动更新暂停 100 年（36524 天）
/// 自动解锁最大暂停天数并直接写入暂停起止 UTC 时间戳，无需手动前往系统设置下拉选择 5215 周。
/// </summary>
public sealed class PauseWindowsUpdateTweak : ITweakModule
{
    private const string UxSettingsPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const int HundredYearsDays = 36524;

    private static readonly string[] PauseTimeValueNames =
    [
        "PauseFeatureUpdatesStartTime",
        "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime",
        "PauseQualityUpdatesEndTime",
        "PauseUpdatesStartTime",
        "PauseUpdatesExpiryTime"
    ];

    public string Id => "pause_windows_update_100_years";
    public string Title => "暂停 Windows 自动更新 100 年";
    public string Description => "解锁 36524 天最大暂停上限并直接写入暂停到期时间戳，无需手动去系统设置翻下拉列表；保留更新底层服务，不影响微软商店下载。";
    public string TechnicalDetail => @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings (FlightSettingsMaxPauseDays=36524 + PauseUpdatesExpiryTime)";
    public string Category => "系统与更新";
    public string IconGlyph => "\uE895"; // Sync / Update icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var expiryRaw = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, UxSettingsPath, "PauseUpdatesExpiryTime")?.ToString();
            var maxDaysObj = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, UxSettingsPath, "FlightSettingsMaxPauseDays");
            int maxDays = maxDaysObj is int d ? d : 0;

            if (!string.IsNullOrWhiteSpace(expiryRaw) &&
                DateTimeOffset.TryParse(expiryRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiryDate))
            {
                if (expiryDate > DateTimeOffset.UtcNow.AddYears(5))
                {
                    return new TweakStatus(true, $"已暂停至 {expiryDate.Year}-{expiryDate.Month:D2}-{expiryDate.Day:D2}");
                }
                if (expiryDate > DateTimeOffset.UtcNow)
                {
                    return new TweakStatus(false, $"短期暂停至 {expiryDate:yyyy-MM-dd}");
                }
            }

            if (maxDays >= 36500)
                return new TweakStatus(false, "仅解锁上限（未激活暂停）");

            return new TweakStatus(false, "正常自动更新（默认）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var endUtc = nowUtc.AddDays(HundredYearsDays);
                string startStr = nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                string endStr = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

                log($"正在设置最大允许暂停天数 FlightSettingsMaxPauseDays = {HundredYearsDays} (100 年)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, UxSettingsPath, "FlightSettingsMaxPauseDays", HundredYearsDays);

                log($"正在写入更新暂停起止时间戳：{startStr} -> {endStr}...");
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseFeatureUpdatesStartTime", startStr);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseFeatureUpdatesEndTime", endStr);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseQualityUpdatesStartTime", startStr);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseQualityUpdatesEndTime", endStr);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseUpdatesStartTime", startStr);
                SystemHelper.SetRegistryString(RegistryHive.LocalMachine, UxSettingsPath, "PauseUpdatesExpiryTime", endStr);

                return new TweakResult(true, $"已将 Windows 更新直接暂停至 {endUtc:yyyy年MM月dd日}（约 100 年）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"暂停更新设置失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在清除更新暂停时间戳并恢复系统默认自动更新设置...");
                SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, UxSettingsPath, "FlightSettingsMaxPauseDays");
                foreach (var name in PauseTimeValueNames)
                {
                    SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, UxSettingsPath, name);
                }
                return new TweakResult(true, "已恢复 Windows 默认自动更新策略。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复更新设置失败：{ex.Message}");
            }
        });
    }
}
