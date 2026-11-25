using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 功能 6：一键解锁并深度调优 Windows「卓越性能」(Ultimate Performance) 电源模式
/// 参考 GameX v2 参数体系（适配 Ryzen 5000+ / Intel 12+ 大小核架构），对 CPU 核心停放、睿频自主模式、
/// 异构线程调度、NVMe 零延迟、PCIe ASPM、USB 防断连、空闲防深睡等 92 项底层参数进行毫秒级注入。
/// </summary>
public sealed partial class UltimatePerformancePowerTweak : ITweakModule
{
    private const string PowerSchemesKeyPath = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    private const string AppStateKeyPath = @"Software\WinOptimizer";
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string BalancedSchemeGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    // 用于校验「卓越性能」是否已完成 GameX v2 级深度调优的特征子项
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SetTimeCheckInterval = "4d2b0152-7d5c-498b-88e2-34345392a2c5"; // GameX v2 = 5000

    [GeneratedRegex(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

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

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerWriteFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        string buffer,
        uint bufferSizeInBytes);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerWriteDescription(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        string buffer,
        uint bufferSizeInBytes);

    private readonly record struct PowerParamRule(string SubGroup, string Setting, uint? AcValue, uint? DcValue);

    /// <summary>
    /// 完整提取自 GameX v2 的 92 项硬核电源调优参数（AC 交流供电 / DC 电池供电）
    /// </summary>
    private static readonly PowerParamRule[] GameXV2Rules =
    [
        // ================= 1. 处理器电源管理（适配 Ryzen 5000+ & Intel 12+ 异构大小核） =================
        new(SubProcessor, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100u, 100u),      // 最小处理器状态 = 100%
        new(SubProcessor, "893dee8e-2bef-41e0-89c6-b55d0929964d", 100u, 100u),      // 第 1 类能效核最小处理器状态 = 100%
        new(SubProcessor, "bc5038f7-23e0-4960-96da-33abaf5935ec", 100u, 100u),      // 最大处理器状态 = 100%
        new(SubProcessor, "0cc5b647-c1df-4637-891a-dec35c318583", 100u, 100u),      // 核心停放最小未停放核心数 = 100%（全面解除核心停放）
        new(SubProcessor, "0cc5b647-c1df-4637-891a-dec35c318584", 100u, 100u),      // 第 1 类核心最小未停放核心数 = 100%
        new(SubProcessor, "447235c7-6a8d-4cc0-8e24-9eaf70b96e2b", 0u, 0u),          // 停放核心性能状态 = 0
        new(SubProcessor, "447235c7-6a8d-4cc0-8e24-9eaf70b96e2c", 0u, 0u),          // 第 1 类停放核心性能状态 = 0
        new(SubProcessor, "e0007330-f589-42ed-a401-5ddb10e785d3", 0u, 0u),          // 核心停放效用分配 = 关闭
        new(SubProcessor, "4bdaf4e9-d103-46d7-a5f0-6280121616ef", 100u, 100u),      // 核心停放分配阈值 = 100%
        new(SubProcessor, "943c8cb6-6f93-4227-ad87-e9a3feec08d1", 100u, 100u),      // 核心停放过度利用阈值 = 100%
        new(SubProcessor, "c7be0679-2817-4d69-9d02-519a537ed0c6", 2u, 2u),          // 核心停放增加策略 = 2 (极速全核唤醒)
        new(SubProcessor, "71021b41-c749-4d21-be74-a00f335d582b", 1u, null),        // 核心停放减少策略 = 1
        new(SubProcessor, "2ddd5a84-5a71-437e-912a-db0b8c788732", 100u, 100u),      // 核心停放增加时间 = 100
        new(SubProcessor, "dfd10d17-d5eb-45dd-877a-9a34ddd15c82", 100u, 100u),      // 核心停放减少时间 = 100
        new(SubProcessor, "f735a673-2066-4f80-a0c5-ddee0cf1bf5d", 10u, null),       // 核心停放并发空间阈值 = 10

        new(SubProcessor, "45bcc044-d885-43e2-8605-ee0ec6e96b59", 100u, 100u),      // 处理器性能增强策略 (Boost Policy) = 100%
        new(SubProcessor, "8baa4a8a-14c6-4451-8e8b-14bdbd197537", 1u, 1u),          // 硬件自主模式 (SpeedShift / CPPC2) = 已启用
        new(SubProcessor, "cfeda3d0-7697-4566-a922-a9086cd49dfa", 45000u, 45000u),  // 自主活动时间窗 = 45000
        new(SubProcessor, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 5u, 0u),          // 能源性能首选项 EPP = AC 5 / DC 0（兼顾最高单核睿频峰值）
        new(SubProcessor, "36687f9e-e3a5-4dbf-b1dc-15eb381c6864", 5u, 0u),          // 第 1 类能效核 EPP = AC 5 / DC 0
        new(SubProcessor, "68dd2f27-a4ce-4e11-8487-3794e4135dfa", 5u, null),        // 扩展能效倾向控制 1 = 5
        new(SubProcessor, "df142941-20f3-4edf-9a4a-9c83d3d717d1", 5u, null),        // 扩展能效倾向控制 2 = 5
        new(SubProcessor, "3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb", 0u, 0u),          // 允许节流状态 = 关闭（禁止处理器热节流以外软降频）
        new(SubProcessor, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1u, 1u),          // 系统散热方式 = 主动

        new(SubProcessor, "06cadf0e-64ed-448a-8927-ce7bf90eb35d", 1u, 1u),          // 性能提高阈值 = 1%
        new(SubProcessor, "06cadf0e-64ed-448a-8927-ce7bf90eb35e", 1u, 1u),          // 第 1 类性能提升阈值 = 1%
        new(SubProcessor, "12a0ab44-fe28-4fa9-b3bd-4b64f44960a6", 1u, 1u),          // 性能降低阈值 = 1%
        new(SubProcessor, "12a0ab44-fe28-4fa9-b3bd-4b64f44960a7", 1u, 1u),          // 第 1 类性能降低阈值 = 1%
        new(SubProcessor, "465e1f50-b610-473a-ab58-00d1077dc418", 2u, 2u),          // 性能提升策略 = 2 (Rocket 极速升频)
        new(SubProcessor, "465e1f50-b610-473a-ab58-00d1077dc419", 2u, null),        // 第 1 类性能提升策略 = 2
        new(SubProcessor, "40fbefc7-2e9d-4d25-a185-0cfd8574bac6", 1u, 1u),          // 性能降低策略 = 1 (Single 缓降)
        new(SubProcessor, "40fbefc7-2e9d-4d25-a185-0cfd8574bac7", 0u, 1u),          // 第 1 类性能降低策略 = 0 / 1
        new(SubProcessor, "984cf492-3bed-4488-a8f9-4286c97bf5aa", 100u, 100u),      // 性能提升时间 = 100
        new(SubProcessor, "984cf492-3bed-4488-a8f9-4286c97bf5ab", 100u, 100u),      // 第 1 类性能提升时间 = 100
        new(SubProcessor, "d8edeb9b-95cf-4f95-a73c-b061973693c8", 100u, 100u),      // 性能下降时间 = 100
        new(SubProcessor, "d8edeb9b-95cf-4f95-a73c-b061973693c9", 100u, 100u),      // 第 1 类性能下降时间 = 100
        new(SubProcessor, "7f2492b6-60b1-45e5-ae55-773f8cd5caec", 100u, 100u),      // 第 1 类扩展下降时间 = 100
        new(SubProcessor, SetTimeCheckInterval, 5000u, 5000u),                      // 性能时间检查间隔 = 5000
        new(SubProcessor, "7d24baa7-0b84-480f-840c-1b0743c00f5f", 128u, 128u),      // 性能历史计数 = 128
        new(SubProcessor, "7d24baa7-0b84-480f-840c-1b0743c00f60", 128u, 128u),      // 第 1 类历史计数 = 128

        new(SubProcessor, "93b8b6dc-0698-4d1c-9ee4-0644e900c85d", 5u, 5u),          // 异类线程调度策略 = 5 (自动高性能异构调度)
        new(SubProcessor, "bae08b81-2d5e-4688-ad6a-13243356654b", 1u, 1u),          // 异类短运行线程调度 = 1 (优先大核/高频核心)
        new(SubProcessor, "616cdaa5-695e-4545-97ad-97dc2d1bdd88", 100u, 100u),      // 延迟敏感度提示最小活跃核心 = 100%
        new(SubProcessor, "616cdaa5-695e-4545-97ad-97dc2d1bdd89", 100u, 100u),      // 第 1 类延迟敏感度最小活跃核心 = 100%
        new(SubProcessor, "619b7505-003b-4e82-b7a6-4dd29c300971", 100u, 100u),      // 延迟敏感度提示处理器性能 = 100%
        new(SubProcessor, "619b7505-003b-4e82-b7a6-4dd29c300972", 100u, 100u),      // 第 1 类延迟敏感度处理器性能 = 100%
        new(SubProcessor, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 0u, null),        // 保留 C0/C1 浅空闲以允许新处理器触发最高单核睿频
        new(SubProcessor, "4b92d758-5a24-4851-a470-815d78aee119", 100u, 100u),      // 闲置降级阈值 = 100%（阻止坠入深层 C6 睡眠造成微卡顿）
        new(SubProcessor, "7b224883-b3cc-4d79-819f-8374152cbe7c", 100u, 100u),      // 闲置升级阈值 = 100%
        new(SubProcessor, "c4581c31-89ab-4597-8e2b-9c9cab440e6b", 200000u, 200000u),// 闲置时间检查 = 200000us

        // ================= 2. 硬盘与 NVMe 固态零延迟调优 =================
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0u, 0u),       // 从不关闭硬盘
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "0b2d69d7-a2a1-449c-9680-f91c70521c60", 0u, 0u),       // AHCI HIPM/DIPM = Active 无休眠
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "dab60367-53fe-4fbc-825e-521d069d2456", 0u, 0u),       // AHCI Adaptive = 0
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "80e3c60e-bb94-4ad8-bbe0-0d3195efc663", 0u, 0u),       // 硬盘脉冲忽略时间 = 0
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "fc95af4d-40e7-4b6d-835a-56d131dbc80e", 0u, 0u),       // 主 NVMe 电源转换延迟容忍度 = 0ms
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "dbc9e238-6de9-49e3-92cd-8c2b4946b472", 0u, 0u),       // 副 NVMe 电源转换延迟容忍度 = 0ms
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "d639518a-e56d-4345-8af2-b9f32fb26109", 200u, 200u),   // 主 NVMe 空闲超时 = 200ms
        new("0012ee47-9041-4b5d-9b77-535fba8b1442", "d3d55efd-c1ff-424e-9dc3-441be7833010", 2000u, 2000u), // 副 NVMe 空闲超时 = 2000ms

        // ================= 3. USB 键鼠外设防断连与低输入延迟 =================
        new("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0u, 0u),       // 禁用 USB 选择性暂停
        new("2a737441-1930-4402-8d77-b2bebba308a3", "d4e98f31-5ffe-4ce1-be31-1b38b384c009", 0u, 0u),       // 禁用 USB 3.0 链路电源管理
        new("2a737441-1930-4402-8d77-b2bebba308a3", "0853a681-27c8-4100-a2fd-82013e970683", 50u, 100000u), // Hub 选择性挂起超时
        new("2a737441-1930-4402-8d77-b2bebba308a3", "498c044a-201b-4631-a522-5c744ed4e678", 1u, 1u),       // Setting IOC on all TDs = 1

        // ================= 4. PCIe 总线、中断转向与空闲弹性 =================
        new("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0u, 0u),       // PCI Express 链接状态电源管理 (ASPM) = 关闭
        new("48672f38-7a9a-4bb2-8bf8-3d85be19de4e", "73cde64d-d720-4bb2-a860-c755afe77ef2", 10000u, 10000u),// 中断转向目标负载 = 10000
        new("48672f38-7a9a-4bb2-8bf8-3d85be19de4e", "d6ba4903-386f-4c2c-8adb-5c21b3328d25", 0u, 0u),       // 中断转向未停放时间触发器 = 0
        new("2e601130-5351-4d9d-8e04-252966bad054", "3166bc41-7e98-4e03-b34e-ec0f5f2b218e", 4294967295u, 4294967295u), // 执行电源请求超时 = 无限
        new("2e601130-5351-4d9d-8e04-252966bad054", "c36f0eb4-2988-4a70-8eee-0884fc2c2433", 0u, 0u),       // IO 合并超时 = 0
        new("2e601130-5351-4d9d-8e04-252966bad054", "d502f7ee-1dc7-4efd-a55d-f04b6f5c0545", 0u, 0u),       // 禁用深度睡眠
        new("44f3beca-a7c0-460e-9df2-bb8b99e0cba6", "3619c3f2-afb2-4afc-b0e9-e7fef372de36", 2u, 2u),       // 核显/显卡电源策略 = 最高性能

        // ================= 5. 睡眠、显示、网络与多媒体全速策略 =================
        new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0u, 0u),       // 无线网卡节能模式 = 最高性能
        new("02f815b5-a5cf-4c84-bf20-649d1f75d3d8", "4c793e7d-a264-42e1-87d3-7a0d2f523ccd", 1u, 1u),       // JavaScript 计时器频率 = 最高性能
        new("0d7dbae2-4294-402a-ba8e-26777e8488cd", "309dce9b-bef4-4119-9921-a851fb12f0f4", 1u, 1u),       // 桌面背景幻灯片 = 可用
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0u, 0u),       // 从不自动睡眠
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "9d7815a6-7ee4-497e-8888-515a05f02364", 0u, 0u),       // 从不自动休眠
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0u, 0u),       // 禁用唤醒定时器
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "25dfa149-5dd1-4736-b5ab-e8a37b5b8187", 1u, 1u),       // 允许离开模式
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d", 0u, 0u),       // 远程打开文件时禁止睡眠
        new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "1a34bdc3-7e6b-442e-a9d0-64b6ef378e84", null, 1u),     // Legacy RTC mitigations
        new("4f971e89-eebd-4455-a8de-9e59040e7347", "5ca83367-6e45-459f-a27b-476b1d01c936", 1u, 0u),       // 合盖操作
        new("4f971e89-eebd-4455-a8de-9e59040e7347", "7648efa3-dd9c-4e3e-b566-50f929386280", 3u, 0u),       // 电源按钮操作
        new("4f971e89-eebd-4455-a8de-9e59040e7347", "96996bc0-ad50-47ec-923b-6f41874dd9eb", 1u, 0u),       // 睡眠按钮操作
        new("7516b95f-f776-4464-8c53-06167f40cc99", "17aaa29b-8b43-4b94-aafe-35f64daaf1ee", 0u, 0u),       // 从不变暗显示器
        new("7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", 0u, 0u),       // 从不关闭显示器
        new("7516b95f-f776-4464-8c53-06167f40cc99", "684c3e69-a4f7-4014-8754-d45179a56167", 1u, 1u),       // 高级色彩质量偏好 = 1
        new("7516b95f-f776-4464-8c53-06167f40cc99", "aded5e82-b909-4619-9949-f5d71dac0bcb", 100u, 100u),   // 显示器亮度 = 100%
        new("8619b916-e004-4dd8-9b66-dae86f806698", "60c07fe1-0556-45cf-9903-d56e32210242", 0u, 0u),       // 待机预算宽限期 = 0
        new("8619b916-e004-4dd8-9b66-dae86f806698", "82011705-fb95-4d46-8d35-4042b1d20def", 0u, 0u),       // 用户状态预测模式 = 0
        new("8619b916-e004-4dd8-9b66-dae86f806698", "9fe527be-1b70-48da-930d-7bcf17b44990", 0u, 0u),       // 待机预算百分比 = 0
        new("8619b916-e004-4dd8-9b66-dae86f806698", "c763ee92-71e8-4127-84eb-f6ed043a3e3d", 0u, 0u),       // 待机保留宽限期 = 0
        new("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "10778347-1370-4ee0-8bbd-33bdacaade49", 1u, 1u),       // 视频播放质量偏向 = 高画质性能
        new("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4", 0u, 0u),       // 播放视频时 = 优化视频质量
        new("de830923-a562-41af-a086-e3a2c6bad2da", "13d09884-f74e-474a-a852-b6bde8ad03a8", 100u, 100u)    // 显示器亮度权重 = 100%
    ];

    public string Id => "ultimate_performance_power";
    public string Title => "开启电源「卓越性能」满血调优版 (GameX v2 参数)";
    public string Description => "解锁「卓越性能」电源计划并注入全套 92 项 GameX v2 级调优（适配 Ryzen 5000+ / Intel 12+：全核心解除停放、CPPC/SpeedShift 极速睿频、NVMe 0ms 延迟、禁用 PCIe/USB 节能与深睡微卡顿）。";
    public string TechnicalDetail => "powrprof.dll 原生注入 92 项 AC/DC 低延迟策略 (CPU 停放解锁 + EPP=5 + NVMe/PCIe/USB 满血)";
    public string Category => "性能与电源";
    public string IconGlyph => "\uE945";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var activeGuid = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerSchemesKeyPath, "ActivePowerScheme")?.ToString();
            if (string.IsNullOrWhiteSpace(activeGuid))
                return new TweakStatus(false, "未知电源方案");

            var friendlyRaw = SystemHelper.GetRegistryValue(
                RegistryHive.LocalMachine,
                $@"{PowerSchemesKeyPath}\{activeGuid}",
                "FriendlyName")?.ToString() ?? string.Empty;

            bool isUltimate = string.Equals(activeGuid, UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase)
                              || friendlyRaw.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase)
                              || friendlyRaw.Contains("卓越性能", StringComparison.OrdinalIgnoreCase)
                              || friendlyRaw.Contains(",-19,", StringComparison.OrdinalIgnoreCase);

            if (isUltimate)
            {
                bool isDeepTuned = IsSchemeDeepTuned(activeGuid);
                return isDeepTuned
                    ? new TweakStatus(true, "卓越性能（已深度调优）")
                    : new TweakStatus(false, "卓越性能（未深度调优）");
            }

            string cleanName = FormatFriendlySchemeName(friendlyRaw, activeGuid);
            return new TweakStatus(false, $"当前：{cleanName}");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            // 1. 保存原电源方案以便关闭开关时无损还原
            var currentActive = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PowerSchemesKeyPath, "ActivePowerScheme")?.ToString();
            if (!string.IsNullOrWhiteSpace(currentActive))
            {
                var currentFriendly = SystemHelper.GetRegistryValue(
                    RegistryHive.LocalMachine,
                    $@"{PowerSchemesKeyPath}\{currentActive}",
                    "FriendlyName")?.ToString() ?? string.Empty;

                if (!currentFriendly.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase) &&
                    !currentFriendly.Contains("卓越性能", StringComparison.OrdinalIgnoreCase) &&
                    !currentFriendly.Contains(",-19,", StringComparison.OrdinalIgnoreCase))
                {
                    SystemHelper.SetRegistryString(RegistryHive.CurrentUser, AppStateKeyPath, "PreviousPowerScheme", currentActive);
                }
            }

            // 2. 查找或创建「卓越性能」方案（避免重复创建）
            string? targetGuidStr = await FindExistingVisibleUltimateGuidAsync();
            if (string.IsNullOrWhiteSpace(targetGuidStr))
            {
                log("正在从系统模板解锁「卓越性能」电源方案...");
                var (_, dupOut, dupErr) = await SystemHelper.RunPowerShellAsync(
                    $"powercfg /duplicatescheme {UltimateTemplateGuid}",
                    log);

                var match = GuidRegex().Match(dupOut);
                targetGuidStr = match.Success ? match.Value : await FindExistingVisibleUltimateGuidAsync();

                if (string.IsNullOrWhiteSpace(targetGuidStr))
                {
                    return new TweakResult(false, $"未能创建卓越性能电源方案：{dupErr}");
                }
            }
            else
            {
                log($"已定位现有卓越性能方案 ({targetGuidStr})，准备注入 GameX v2 细项参数...");
            }

            // 3. 通过原生 powrprof.dll 高速写入全部 92 项 GameX v2 参数并更新方案描述
            var schemeGuid = Guid.Parse(targetGuidStr);
            int appliedCount = await Task.Run(() =>
            {
                int ok = 0;
                foreach (var rule in GameXV2Rules)
                {
                    var subGuid = Guid.Parse(rule.SubGroup);
                    var setGuid = Guid.Parse(rule.Setting);
                    bool ruleOk = true;

                    if (rule.AcValue.HasValue)
                    {
                        if (PowerWriteACValueIndex(0, in schemeGuid, in subGuid, in setGuid, rule.AcValue.Value) != 0)
                            ruleOk = false;
                    }
                    if (rule.DcValue.HasValue)
                    {
                        if (PowerWriteDCValueIndex(0, in schemeGuid, in subGuid, in setGuid, rule.DcValue.Value) != 0)
                            ruleOk = false;
                    }

                    if (ruleOk) ok++;
                }

                // 若用户已开启「大小核专属：强制大核优先」，自动保持 Policy = 2 不被覆盖
                if (SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AppStateKeyPath, "PreferPCoresEnabled") is int pCore && pCore == 1)
                {
                    var subProc = Guid.Parse(SubProcessor);
                    var longGuid = Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
                    var shortGuid = Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b");
                    PowerWriteACValueIndex(0, in schemeGuid, in subProc, in longGuid, 2u);
                    PowerWriteDCValueIndex(0, in schemeGuid, in subProc, in longGuid, 2u);
                    PowerWriteACValueIndex(0, in schemeGuid, in subProc, in shortGuid, 2u);
                    PowerWriteDCValueIndex(0, in schemeGuid, in subProc, in shortGuid, 2u);
                }

                // 通过 powrprof.dll 官方 Power RPC 服务修改名称与说明（避免直接写 PowerSchemes 注册表触发 NT SERVICE\Power 权限拒绝）
                const string newName = "卓越性能 (GameX 极速调优)";
                const string newDesc = "基于卓越性能深度调优（参考 GameX v2）：全核解锁停放、适配 Ryzen 5000+ / Intel 12+ 异构睿频、NVMe/PCIe/USB 零节能低延迟。";
                _ = PowerWriteFriendlyName(0, in schemeGuid, 0, 0, newName, (uint)((newName.Length + 1) * 2));
                _ = PowerWriteDescription(0, in schemeGuid, 0, 0, newDesc, (uint)((newDesc.Length + 1) * 2));

                return ok;
            });

            log($"已通过原生电源接口写入 {appliedCount} / {GameXV2Rules.Length} 项底层硬件调优参数。");

            // 4. 立即激活该电源方案使内核策略即时生效
            uint activeErr = await Task.Run(() => PowerSetActiveScheme(0, in schemeGuid));
            if (activeErr != 0)
            {
                var (setCode, _, setErr) = await SystemHelper.RunPowerShellAsync($"powercfg /setactive {targetGuidStr}", log);
                if (setCode != 0)
                    return new TweakResult(false, $"激活卓越性能方案失败：{setErr}");
            }

            return new TweakResult(true, $"已激活「卓越性能 (GameX 极速调优)」并生效 {appliedCount} 项满血低延迟参数！");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"开启并调优卓越性能失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            var prevGuid = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AppStateKeyPath, "PreviousPowerScheme")?.ToString();
            string targetGuid = (!string.IsNullOrWhiteSpace(prevGuid) &&
                                 SystemHelper.SubKeyExists(RegistryHive.LocalMachine, $@"{PowerSchemesKeyPath}\{prevGuid}"))
                ? prevGuid
                : BalancedSchemeGuid;

            log($"正在还原至切换前的电源计划 ({targetGuid})...");
            var guid = Guid.Parse(targetGuid);
            uint err = await Task.Run(() => PowerSetActiveScheme(0, in guid));
            if (err != 0)
            {
                await SystemHelper.RunPowerShellAsync($"powercfg /setactive {BalancedSchemeGuid}", log);
            }

            return new TweakResult(true, "已切回原先的电源使用方案。");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复电源计划失败：{ex.Message}");
        }
    }

    private static bool IsSchemeDeepTuned(string schemeGuid)
    {
        var checkVal = SystemHelper.GetRegistryValue(
            RegistryHive.LocalMachine,
            $@"{PowerSchemesKeyPath}\{schemeGuid}\{SubProcessor}\{SetTimeCheckInterval}",
            "ACSettingIndex");
        return checkVal is int v && v == 5000;
    }

    private static async Task<string?> FindExistingVisibleUltimateGuidAsync()
    {
        var (_, listOut, _) = await SystemHelper.RunPowerShellAsync("powercfg /list");
        foreach (Match m in GuidRegex().Matches(listOut))
        {
            string guid = m.Value;
            var friendly = SystemHelper.GetRegistryValue(
                RegistryHive.LocalMachine,
                $@"{PowerSchemesKeyPath}\{guid}",
                "FriendlyName")?.ToString() ?? string.Empty;

            if (string.Equals(guid, UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase) ||
                friendly.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
                friendly.Contains("卓越性能", StringComparison.OrdinalIgnoreCase) ||
                friendly.Contains(",-19,", StringComparison.OrdinalIgnoreCase))
            {
                return guid;
            }
        }
        return null;
    }

    private static string FormatFriendlySchemeName(string raw, string guid)
    {
        if (guid.Equals(BalancedSchemeGuid, StringComparison.OrdinalIgnoreCase) || raw.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
            return "平衡";
        if (guid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase) || raw.Contains("High performance", StringComparison.OrdinalIgnoreCase))
            return "高性能";
        if (guid.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase) || raw.Contains("Power saver", StringComparison.OrdinalIgnoreCase))
            return "节能";

        if (raw.StartsWith('@'))
        {
            int lastComma = raw.LastIndexOf(',');
            if (lastComma >= 0 && lastComma + 1 < raw.Length)
                return raw[(lastComma + 1)..].Trim();
        }

        return string.IsNullOrWhiteSpace(raw) ? guid[..8] : raw;
    }
}
