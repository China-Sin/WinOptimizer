using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 4：Intel 12/13/14 代大小核专属 — 强制游戏线程优先调度至高频大核 (P-Core)
/// 使用 Windows 内核电源调度器合法策略 (Prefer Performant Processors = 2)，免关小核，100% 兼容反作弊。
/// </summary>
public sealed partial class HeterogeneousPCoreSchedulingTweak : ITweakModule
{
    private const string PowerSchemesKeyPath = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid SetHeteroLongThreadPolicy = new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
    private static readonly Guid SetHeteroShortThreadPolicy = new("bae08b81-2d5e-4688-ad6a-13243356654b");

    // 2 = Prefer performant processors (优先高频大核 P-Core)；5 = Automatic (系统自动混合调度)
    private const uint PreferPerformantCores = 2u;
    private const uint AutomaticScheduling = 5u;

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteACValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        uint acValueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDCValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        uint dcValueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(
        nint userRootPowerKey,
        in Guid schemeGuid);

    public string Id => "intel_hybrid_pcore_prefer_scheduling";
    public string Title => "大小核专属：强制游戏线程优先跑满大核 (P-Core)";
    public string Description => "针对 i7-13700K 等混合架构处理器，将内核长/短线程异构调度设为「优先高性能处理器」，防止永劫团战时 Unity 线程掉入小核拖垮 1% Low 帧（实时生效，零反作弊风险）。";
    public string TechnicalDetail => "Heterogeneous thread scheduling policy (93b8b6dc / bae08b81) = 2 (Prefer Performant P-Cores)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE950";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var activeGuid = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerSchemesKeyPath, "ActivePowerScheme")?.ToString();
            if (string.IsNullOrWhiteSpace(activeGuid))
                return new TweakStatus(false, "未知电源方案");

            var longVal = SystemHelper.GetRegistryValue(
                RegistryHive.LocalMachine,
                $@"{PowerSchemesKeyPath}\{activeGuid}\{SubProcessor}\{SetHeteroLongThreadPolicy}",
                "ACSettingIndex");

            bool preferPCores = longVal is int v && v == (int)PreferPerformantCores;
            return preferPCores
                ? new TweakStatus(true, "大核优先（已实时生效）")
                : new TweakStatus(false, "自动混合调度（可能进小核）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var activeStr = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerSchemesKeyPath, "ActivePowerScheme")?.ToString();
                if (string.IsNullOrWhiteSpace(activeStr) || !Guid.TryParse(activeStr, out var schemeGuid))
                    return new TweakResult(false, "无法获取当前活动电源方案 GUID。");

                log($"正在为当前活动电源方案 ({activeStr}) 设置长/短线程强制倾向高频大核 (Policy = 2)...");
                PowerWriteACValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroLongThreadPolicy, PreferPerformantCores);
                PowerWriteDCValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroLongThreadPolicy, PreferPerformantCores);
                PowerWriteACValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroShortThreadPolicy, PreferPerformantCores);
                PowerWriteDCValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroShortThreadPolicy, PreferPerformantCores);

                PowerSetActiveScheme(0, in schemeGuid);
                SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, @"Software\WinOptimizer", "PreferPCoresEnabled", 1);
                return new TweakResult(true, "已开启内核级「大核 (P-Core) 优先调度」，当前已实时生效！");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"设置大小核调度策略失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var activeStr = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerSchemesKeyPath, "ActivePowerScheme")?.ToString();
                if (string.IsNullOrWhiteSpace(activeStr) || !Guid.TryParse(activeStr, out var schemeGuid))
                    return new TweakResult(false, "无法获取当前活动电源方案 GUID。");

                log("正在将处理器异构线程调度恢复为系统自动分配模式 (Automatic = 5)...");
                PowerWriteACValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroLongThreadPolicy, AutomaticScheduling);
                PowerWriteDCValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroLongThreadPolicy, AutomaticScheduling);
                PowerWriteACValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroShortThreadPolicy, 1u);
                PowerWriteDCValueIndex(0, in schemeGuid, in SubProcessor, in SetHeteroShortThreadPolicy, 1u);

                PowerSetActiveScheme(0, in schemeGuid);
                SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, @"Software\WinOptimizer", "PreferPCoresEnabled");
                return new TweakResult(true, "已恢复系统默认的大小核自动混合调度策略。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
