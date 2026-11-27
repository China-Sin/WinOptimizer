using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 7：锁死系统内核常驻物理内存 (DisablePagingExecutive=1) + 内置 ISLC 同款原生备用内存 (Standby List) 清理引擎
/// </summary>
public sealed partial class MemoryResidentAndStandbyPurgeTweak : ITweakModule
{
    private const string MemoryMgmtPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(
        int systemInformationClass,
        ref int systemInformation,
        int systemInformationLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        nint previousState,
        nint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public string Id => "kernel_resident_and_standby_ram";
    public string Title => "锁死内核常驻物理内存 & 释放备用缓存内存";
    public string Description => "开启 DisablePagingExecutive=1 强制系统内核与显卡/键鼠驱动常驻 DDR4/DDR5 内存不写入虚拟内存，并立即调用 NtSetSystemInformation 清理堆积的备用内存防久玩掉帧。";
    public string TechnicalDetail => @"DisablePagingExecutive=1 + NtSetSystemInformation(MemoryPurgeStandbyList)";
    public string Category => "性能与电源";
    public string IconGlyph => "\uE950";
    public bool NeedsExplorerRestart => false;
    public TweakRestartRequirement RestartRequirement => TweakRestartRequirement.SystemReboot;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var val = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, MemoryMgmtPath, "DisablePagingExecutive");
            bool enabled = val is int v && v == 1;
            return enabled
                ? new TweakStatus(true, "内核已常驻物理内存")
                : new TweakStatus(false, "允许换出至磁盘（默认）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            log("正在设置 DisablePagingExecutive = 1（锁定内核与驱动常驻物理内存）...");
            SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, MemoryMgmtPath, "DisablePagingExecutive", 1);

            var (_, msg) = await PurgeStandbyListAsync(log);
            return new TweakResult(true, $"已开启内核常驻物理内存！{msg}");
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置内存优化失败：{ex.Message}");
        }
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                log("正在恢复 DisablePagingExecutive = 0（系统默认内核分页策略）...");
                SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, MemoryMgmtPath, "DisablePagingExecutive", 0);
                return new TweakResult(true, "已恢复系统默认内存分页设置。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 原生 ISLC 同款接口：秒清 Windows Standby List 备用缓存内存（也可供界面快捷按钮随时调用）
    /// </summary>
    public static Task<(bool Success, string Summary)> PurgeStandbyListAsync(Action<string>? log = null)
    {
        return Task.Run(() =>
        {
            double beforeGb = GetAvailablePhysicalMemoryGb();
            log?.Invoke($"正在申请 SeProfileSingleProcessPrivilege 权限并清理系统备用内存列表 (当前可用内存：{beforeGb:F2} GB)...");

            if (!EnableProfilePrivilege())
            {
                return (false, "获取内存清理特权失败，请确保以管理员权限运行。");
            }

            int cmd = MemoryPurgeStandbyList;
            int ntStatus = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
            if (ntStatus != 0)
            {
                return (false, $"NtSetSystemInformation 返回状态码 0x{ntStatus:X8}。");
            }

            double afterGb = GetAvailablePhysicalMemoryGb();
            double deltaMb = Math.Max(0, (afterGb - beforeGb) * 1024.0);
            return (true, $"备用缓存内存已清理，当前可用物理内存：{beforeGb:F2} GB → {afterGb:F2} GB (释放约 {deltaMb:F0} MB)。");
        });
    }

    private static double GetAvailablePhysicalMemoryGb()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref mem) ? mem.ullAvailPhys / 1073741824.0 : 0.0;
    }

    private static bool EnableProfilePrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out nint hToken))
            return false;

        try
        {
            if (!LookupPrivilegeValueW(null, "SeProfileSingleProcessPrivilege", out LUID luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };

            return AdjustTokenPrivileges(hToken, false, ref tp, 0, 0, 0);
        }
        finally
        {
            CloseHandle(hToken);
        }
    }
}
