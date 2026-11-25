using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 5：前台游戏时间片倾斜 (Win32PrioritySeparation=0x26) + NarakaBladepoint.exe 系统级高优先级预设
/// </summary>
public sealed class ForegroundAndNarakaPriorityTweak : ITweakModule
{
    private const string PriorityControlPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string NarakaIfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\NarakaBladepoint.exe";
    private const string NarakaPerfPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\NarakaBladepoint.exe\PerfOptions";
    private const string AppStateKeyPath = @"Software\WinOptimizer";
    private const int OptimalSeparation = 0x26; // 38: 短量子 + 固定长度 + 最高 3:1 前台焦点突发权重

    public string Id => "foreground_quantum_and_naraka_priority";
    public string Title => "前台 3 倍时间片倾斜 &《永劫无间》专属高优先级";
    public string Description => "设置 Win32PrioritySeparation=0x26 让前台焦点游戏获得最高突发 CPU 响应，并通过官方 PerfOptions 为 NarakaBladepoint.exe 预设高 CPU 与高 I/O 优先级。";
    public string TechnicalDetail => @"Win32PrioritySeparation=0x26 (38) + IFEO\NarakaBladepoint.exe\PerfOptions (CpuPriorityClass=3)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7FC";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var sepObj = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PriorityControlPath, "Win32PrioritySeparation");
            var cpuPrio = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, NarakaPerfPath, "CpuPriorityClass");

            bool sepOk = sepObj is int s && s == OptimalSeparation;
            bool narakaOk = cpuPrio is int c && c == 3;

            if (sepOk && narakaOk)
                return new TweakStatus(true, "0x26 极速响应 + 永劫高优");
            if (sepOk || narakaOk)
                return new TweakStatus(false, "部分开启");

            return new TweakStatus(false, sepObj is int cur ? $"当前时间片：0x{cur:X2}" : "系统默认 (0x02)");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var currentSep = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, PriorityControlPath, "Win32PrioritySeparation");
                if (currentSep is int cur && cur != OptimalSeparation)
                {
                    SystemHelper.SetRegistryDword(RegistryHive.CurrentUser, AppStateKeyPath, "PreviousPrioritySeparation", cur);
                }

                log("正在设置内核前台时间片调度 Win32PrioritySeparation = 0x26 (十进制 38，实时生效)...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PriorityControlPath, "Win32PrioritySeparation", OptimalSeparation);

                log("正在为 NarakaBladepoint.exe 写入系统级高 CPU (3) 与高 I/O (3) 调度优先级...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, NarakaPerfPath, "CpuPriorityClass", 3);
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, NarakaPerfPath, "IoPriority", 3);

                return new TweakResult(true, "已激活 0x26 前台极速时间片调度，并已预设《永劫无间》主进程高优先级！");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"设置优先级失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var prev = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, AppStateKeyPath, "PreviousPrioritySeparation");
                int restoreVal = prev is int p ? p : 2;

                log($"正在恢复 Win32PrioritySeparation = 0x{restoreVal:X2} 并移除 NarakaBladepoint.exe 自定义优先级...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, PriorityControlPath, "Win32PrioritySeparation", restoreVal);
                SystemHelper.DeleteRegistrySubKeyTree(RegistryHive.LocalMachine, NarakaPerfPath);

                // 如果 NarakaBladepoint.exe 下无其它子项（如调试项），顺便清理空键
                using var hklm = SystemHelper.OpenBaseKey(RegistryHive.LocalMachine);
                using var narakaKey = hklm.OpenSubKey(NarakaIfeoRoot, writable: false);
                if (narakaKey != null && narakaKey.SubKeyCount == 0 && narakaKey.ValueCount == 0)
                {
                    narakaKey.Close();
                    SystemHelper.DeleteRegistrySubKeyTree(RegistryHive.LocalMachine, NarakaIfeoRoot);
                }

                return new TweakResult(true, "已恢复原系统时间片调度权重与《永劫无间》默认进程优先级。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }
}
