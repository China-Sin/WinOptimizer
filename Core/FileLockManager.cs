using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinOptimizer.Core;

public enum LockDetectMethod
{
    RestartManager,
    LoadedDllModule,
    OpenDiskHandle
}

public sealed class FileLockInfo
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ProcessPath { get; init; }
    public required string LockedTargetPath { get; init; }
    public required LockDetectMethod Method { get; init; }
    public string? ServiceShortName { get; init; }
    public List<nint> HandleValues { get; } = [];
    public bool IsExplorer => ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
    public bool IsCriticalSystemProcess { get; init; }

    public string MethodBadge => Method switch
    {
        LockDetectMethod.OpenDiskHandle => $"文件句柄 ({HandleValues.Count})",
        LockDetectMethod.LoadedDllModule => "DLL 模块映射",
        _ => "Restart Manager 锁定"
    };

    public bool CanCloseHandleOnly => HandleValues.Count > 0 && Method != LockDetectMethod.LoadedDllModule;
}

/// <summary>
/// Windows 文件/文件夹占用检测与一键解除/强删引擎。
/// 融合三重检测：
/// 1. Windows Restart Manager API (rstrtmgr.dll) — 精准捕获普通文件与服务锁；
/// 2. 全进程模块扫描 (Process.Modules) — 专门捕获被 LoadLibrary 映射的 .dll / .exe（如 AdobeGrowthSDK.dll）；
/// 3. 内核扩展句柄表枚举 (NtQuerySystemInformation 64 + GetFinalPathNameByHandleW) — 捕获文件夹占用并支持「不杀进程仅断开句柄」。
/// </summary>
public static partial class FileLockManager
{
    private const uint FileTypeDisk = 1;
    private const uint ProcessDupHandle = 0x0040;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint DuplicateCloseSource = 0x00000001;
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint MoveFileDelayUntilReboot = 0x00000004;
    private const int SystemExtendedHandleInformation = 64;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int ErrorMoreData = 234;

    private const string FileContextKey = @"Software\Classes\*\shell\WinOptimizerUnlock";
    private const string DirContextKey = @"Software\Classes\Directory\shell\WinOptimizerUnlock";

    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "dwm", "fontdrvhost"
    };

    #region Native P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public nint Object;
        public nuint UniqueProcessId;
        public nuint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        nint rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        int systemInformationClass,
        nint systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(nint hFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(nint hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(nint tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TOKEN_PRIVILEGES newState, uint bufLen, nint prev, nint retLen);

    #endregion

    public static string NormalizeTargetPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string cleaned = raw.Trim().Trim('"', '\'').Trim();
        try
        {
            return Path.GetFullPath(cleaned);
        }
        catch
        {
            return cleaned;
        }
    }

    /// <summary>
    /// 综合三重引擎扫描占用指定文件或文件夹的全部进程与句柄
    /// </summary>
    public static Task<List<FileLockInfo>> ScanLocksAsync(string rawPath, Action<string>? log = null)
    {
        return Task.Run(() =>
        {
            string targetPath = NormalizeTargetPath(rawPath);
            var map = new Dictionary<int, FileLockInfo>();
            if (string.IsNullOrWhiteSpace(targetPath))
                return new List<FileLockInfo>();

            bool isFile = File.Exists(targetPath);
            bool isDir = Directory.Exists(targetPath);
            if (!isFile && !isDir)
            {
                log?.Invoke($"路径不存在：{targetPath}");
                return new List<FileLockInfo>();
            }

            EnableDebugPrivilege();
            int currentPid = Environment.ProcessId;

            // 1. Restart Manager 扫描（对文件夹自动收集前 250 个文件批量检查）
            log?.Invoke("正在通过 Windows Restart Manager 扫描常规文件锁...");
            var rmFiles = CollectFilesForRestartManager(targetPath, isDir, maxFiles: 250);
            if (rmFiles.Count > 0)
            {
                ScanViaRestartManager(rmFiles, targetPath, currentPid, map);
            }

            // 2. 全进程已加载 DLL/EXE 模块扫描（精准抓出 AdobeGrowthSDK.dll 这类被 LoadLibrary 映射的 DLL）
            log?.Invoke("正在扫描全部运行中进程的已加载 DLL/EXE 模块...");
            ScanLoadedModules(targetPath, isDir, currentPid, map);

            // 3. 内核扩展句柄表扫描（精准抓出目录占用与可不杀进程直接断开的单个磁盘文件句柄）
            log?.Invoke("正在深度扫描系统磁盘句柄表（识别文件夹占用与可独立断开的文件句柄）...");
            ScanKernelDiskHandles(targetPath, isDir, currentPid, map);

            return map.Values
                .OrderBy(x => x.IsCriticalSystemProcess ? 1 : 0)
                .ThenBy(x => x.IsExplorer ? 1 : 0)
                .ThenBy(x => x.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });
    }

    /// <summary>
    /// 不杀进程，仅强制关闭目标进程持有的对应文件句柄（类似 Process Explorer / 火绒解除占用）
    /// </summary>
    public static Task<TweakResult> CloseHandlesOnlyAsync(FileLockInfo lockInfo, Action<string> log)
    {
        return Task.Run(() =>
        {
            if (lockInfo.HandleValues.Count == 0)
                return new TweakResult(false, $"[{lockInfo.ProcessName}] 属于 DLL 模块内存映射锁定，无独立可断文件句柄，请直接点击「结束进程」。");

            EnableDebugPrivilege();
            nint hProc = OpenProcess(ProcessDupHandle, false, lockInfo.ProcessId);
            if (hProc == 0)
                return new TweakResult(false, $"无法打开进程 {lockInfo.ProcessName} (PID={lockInfo.ProcessId}) 的句柄操作权限。");

            int closed = 0;
            try
            {
                foreach (var handleVal in lockInfo.HandleValues)
                {
                    if (DuplicateHandle(hProc, handleVal, 0, out _, 0, false, DuplicateCloseSource))
                    {
                        closed++;
                    }
                }
            }
            finally
            {
                CloseHandle(hProc);
            }

            log($"已从进程 {lockInfo.ProcessName} (PID={lockInfo.ProcessId}) 中强制断开 {closed}/{lockInfo.HandleValues.Count} 个占用句柄。");
            return closed > 0
                ? new TweakResult(true, $"已成功断开 [{lockInfo.ProcessName}] 的 {closed} 个文件占用句柄（原进程保持运行未关闭）。")
                : new TweakResult(false, $"句柄可能已被释放或受进程保护，建议改用「结束进程」。");
        });
    }

    /// <summary>
    /// 结束单个占用进程（若为 explorer.exe 则执行安全重启资源管理器；若为关联服务则先停止服务）
    /// </summary>
    public static async Task<TweakResult> TerminateLockingProcessAsync(FileLockInfo lockInfo, Action<string> log)
    {
        if (lockInfo.IsCriticalSystemProcess)
            return new TweakResult(false, $"[{lockInfo.ProcessName}] 属于 Windows 核心系统进程，禁止强制结束（可尝试「仅断开句柄」或「重启时自动删除」）。");

        if (lockInfo.IsExplorer)
        {
            await SystemHelper.RestartExplorerAsync(log);
            return new TweakResult(true, "已安全重启 Windows 资源管理器以释放占用。");
        }

        return await Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(lockInfo.ServiceShortName))
                {
                    log($"检测到占用进程关联系统服务 [{lockInfo.ServiceShortName}]，正在先停止服务防止自动拉起...");
                    await SystemHelper.RunPowerShellAsync(
                        $"Stop-Service -Name '{lockInfo.ServiceShortName.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue",
                        timeoutSeconds: 15);
                }

                using var proc = Process.GetProcessById(lockInfo.ProcessId);
                log($"正在结束占用进程 {lockInfo.ProcessName} (PID={lockInfo.ProcessId})...");
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(4000);
                return new TweakResult(true, $"已成功结束进程 {lockInfo.ProcessName} (PID={lockInfo.ProcessId})。");
            }
            catch (ArgumentException)
            {
                return new TweakResult(true, $"进程 {lockInfo.ProcessName} (PID={lockInfo.ProcessId}) 已自行退出。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"结束进程 {lockInfo.ProcessName} 失败：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 一键解除所有占用（优先无损断开句柄，对 DLL 映射/无法断句柄的普通进程执行结束，对 Explorer 执行安全重启）
    /// </summary>
    public static async Task<TweakResult> UnlockAllAsync(IReadOnlyList<FileLockInfo> locks, Action<string> log)
    {
        if (locks.Count == 0)
            return new TweakResult(true, "当前未检测到占用进程，可直接操作目标文件。");

        int ok = 0;
        bool needExplorerRestart = false;

        foreach (var item in locks)
        {
            if (item.IsExplorer)
            {
                if (item.CanCloseHandleOnly)
                {
                    var hRes = await CloseHandlesOnlyAsync(item, log);
                    if (hRes.Success) { ok++; continue; }
                }
                needExplorerRestart = true;
                continue;
            }

            if (item.IsCriticalSystemProcess)
            {
                if (item.CanCloseHandleOnly)
                {
                    var hRes = await CloseHandlesOnlyAsync(item, log);
                    if (hRes.Success) ok++;
                }
                else
                {
                    log($"跳过关键系统进程 {item.ProcessName} (PID={item.ProcessId}) 以防系统异常。");
                }
                continue;
            }

            // 对纯句柄占用先尝试无损断句柄；对 DLL 模块加载必须结束进程才能释放内存镜像映射
            if (item.CanCloseHandleOnly)
            {
                var hRes = await CloseHandlesOnlyAsync(item, log);
                if (hRes.Success)
                {
                    ok++;
                    continue;
                }
            }

            var killRes = await TerminateLockingProcessAsync(item, log);
            if (killRes.Success) ok++;
        }

        if (needExplorerRestart)
        {
            await SystemHelper.RestartExplorerAsync(log);
            ok++;
        }

        await Task.Delay(350);
        return new TweakResult(true, $"已完成解除占用处理（共处理 {ok}/{locks.Count} 个占用项）。");
    }

    /// <summary>
    /// 强制删除目标文件或文件夹（清除只读/系统属性，失败时自动反馈是否登记重启自动删除）
    /// </summary>
    public static Task<(bool Deleted, string Message)> ForceDeleteTargetAsync(string rawPath, Action<string> log)
    {
        return Task.Run(() =>
        {
            string target = NormalizeTargetPath(rawPath);
            try
            {
                if (File.Exists(target))
                {
                    log($"正在清除文件只读/隐藏属性并执行删除：{target}");
                    File.SetAttributes(target, FileAttributes.Normal);
                    File.Delete(target);
                    return (true, $"已成功删除文件：{target}");
                }

                if (Directory.Exists(target))
                {
                    log($"正在递归清除目录内只读属性并执行删除：{target}");
                    ClearAttributesRecursive(target);
                    Directory.Delete(target, recursive: true);
                    return (true, $"已成功删除整个文件夹：{target}");
                }

                return (true, "目标路径已不存在（无需删除）。");
            }
            catch (Exception ex)
            {
                return (false, $"直接删除仍被系统拒绝（{ex.Message}）。可点击「重启时自动删除」交给系统内核开机第一时间清除。");
            }
        });
    }

    /// <summary>
    /// 登记 Windows 内核级「重启时自动删除」(MoveFileEx MOVEFILE_DELAY_UNTIL_REBOOT)
    /// </summary>
    public static Task<TweakResult> ScheduleDeleteOnRebootAsync(string rawPath, Action<string> log)
    {
        return Task.Run(() =>
        {
            string target = NormalizeTargetPath(rawPath);
            if (!File.Exists(target) && !Directory.Exists(target))
                return new TweakResult(false, "目标路径不存在。");

            if (Directory.Exists(target))
            {
                // MoveFileEx 删除目录要求先排空内部文件，再登记目录本身
                int scheduled = 0;
                foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                {
                    if (MoveFileExW(file, null, MoveFileDelayUntilReboot))
                        scheduled++;
                }
                foreach (var subDir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    MoveFileExW(subDir, null, MoveFileDelayUntilReboot);
                }
                bool dirOk = MoveFileExW(target, null, MoveFileDelayUntilReboot);
                log($"已登记 PendingFileRenameOperations：目录及内部 {scheduled} 个文件将在下次重启时自动删除。");
                return dirOk
                    ? new TweakResult(true, $"已登记下次重启电脑时由 Windows 内核自动删除该文件夹（共 {scheduled} 个文件）。", RecommendSystemReboot: true)
                    : new TweakResult(false, "登记重启删除失败，请检查管理员权限。");
            }
            else
            {
                bool ok = MoveFileExW(target, null, MoveFileDelayUntilReboot);
                log($"已登记 PendingFileRenameOperations：{target} 将在下次重启电脑时自动删除。");
                return ok
                    ? new TweakResult(true, $"已登记下次重启电脑时由 Windows 内核自动删除：{Path.GetFileName(target)}。", RecommendSystemReboot: true)
                    : new TweakResult(false, "登记重启删除失败。");
            }
        });
    }

    #region System Context Menu Integration

    public static bool IsRightClickIntegrationEnabled()
        => SystemHelper.SubKeyExists(RegistryHive.CurrentUser, FileContextKey) &&
           SystemHelper.SubKeyExists(RegistryHive.CurrentUser, DirContextKey);

    public static TweakResult SetRightClickIntegration(bool enable, Action<string> log)
    {
        try
        {
            if (enable)
            {
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exePath))
                    return new TweakResult(false, "无法获取当前可执行文件路径。");

                log($"正在将「使用 WinOptimizer 解除占用」注册到文件与文件夹右键菜单 ({exePath})...");
                foreach (var root in new[] { FileContextKey, DirContextKey })
                {
                    SystemHelper.SetRegistryString(RegistryHive.CurrentUser, root, null, "WinOptimizer 解除占用");
                    SystemHelper.SetRegistryString(RegistryHive.CurrentUser, root, "Icon", $"\"{exePath}\",0");
                    SystemHelper.SetRegistryString(RegistryHive.CurrentUser, $@"{root}\command", null, $"\"{exePath}\" --unlock \"%1\"");
                }
                return new TweakResult(true, "已添加到系统右键菜单！现在右键任意文件或文件夹点击「WinOptimizer 解除占用」即可直达解锁。");
            }
            else
            {
                log("正在从文件与文件夹右键菜单移除「WinOptimizer 解除占用」...");
                SystemHelper.DeleteRegistrySubKeyTree(RegistryHive.CurrentUser, FileContextKey);
                SystemHelper.DeleteRegistrySubKeyTree(RegistryHive.CurrentUser, DirContextKey);
                return new TweakResult(true, "已从系统右键菜单移除解除占用入口。");
            }
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置右键菜单入口失败：{ex.Message}");
        }
    }

    #endregion

    #region Detection Engines

    private static List<string> CollectFilesForRestartManager(string targetPath, bool isDir, int maxFiles)
    {
        if (!isDir) return [targetPath];

        var list = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories))
            {
                list.Add(f);
                if (list.Count >= maxFiles) break;
            }
        }
        catch
        {
            // ignore inaccessible subdirectories
        }
        return list;
    }

    private static void ScanViaRestartManager(List<string> files, string displayTarget, int currentPid, Dictionary<int, FileLockInfo> map)
    {
        if (RmStartSession(out uint session, 0, Guid.NewGuid().ToString("N")) != 0)
            return;

        try
        {
            if (RmRegisterResources(session, (uint)files.Count, files.ToArray(), 0, 0, 0, null) != 0)
                return;

            uint needed = 0, count = 0, reasons = 0;
            int res = RmGetList(session, out needed, ref count, null, ref reasons);
            if (res == ErrorMoreData && needed > 0)
            {
                var infos = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, infos, ref reasons) == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int pid = infos[i].Process.dwProcessId;
                        if (pid <= 0 || pid == currentPid) continue;
                        if (map.ContainsKey(pid)) continue;

                        string procName = TryGetProcessName(pid) ?? infos[i].strAppName;
                        string procPath = TryGetProcessPath(pid);
                        map[pid] = new FileLockInfo
                        {
                            ProcessId = pid,
                            ProcessName = procName,
                            ProcessPath = procPath,
                            LockedTargetPath = displayTarget,
                            Method = LockDetectMethod.RestartManager,
                            ServiceShortName = string.IsNullOrWhiteSpace(infos[i].strServiceShortName) ? null : infos[i].strServiceShortName,
                            IsCriticalSystemProcess = CriticalProcesses.Contains(procName)
                        };
                    }
                }
            }
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private static void ScanLoadedModules(string targetPath, bool isDir, int currentPid, Dictionary<int, FileLockInfo> map)
    {
        string dirPrefix = isDir ? EnsureTrailingSlash(targetPath) : string.Empty;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4 || proc.Id == currentPid) continue;

                foreach (ProcessModule mod in proc.Modules)
                {
                    string? modFile = mod.FileName;
                    if (string.IsNullOrWhiteSpace(modFile)) continue;

                    bool matched = isDir
                        ? modFile.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase)
                        : modFile.Equals(targetPath, StringComparison.OrdinalIgnoreCase);

                    if (matched)
                    {
                        map[proc.Id] = new FileLockInfo
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName,
                            ProcessPath = TryGetProcessPath(proc.Id),
                            LockedTargetPath = modFile,
                            Method = LockDetectMethod.LoadedDllModule,
                            IsCriticalSystemProcess = CriticalProcesses.Contains(proc.ProcessName)
                        };
                        break;
                    }
                }
            }
            catch
            {
                // Protected / 32-bit / exited process
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static void ScanKernelDiskHandles(string targetPath, bool isDir, int currentPid, Dictionary<int, FileLockInfo> map)
    {
        ushort? fileObjectTypeIndex = DetectFileObjectTypeIndex();
        if (fileObjectTypeIndex == null) return;

        uint bufferSize = 1024 * 1024 * 4; // 4 MB initial
        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint status;
            while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out uint needed)) == StatusInfoLengthMismatch)
            {
                Marshal.FreeHGlobal(buffer);
                bufferSize = Math.Max(needed + 65536u, bufferSize * 2);
                buffer = Marshal.AllocHGlobal((int)bufferSize);
            }

            if (status != 0) return;

            long handleCount = Marshal.ReadIntPtr(buffer).ToInt64();
            int entryOffset = nint.Size * 2; // NumberOfHandles + Reserved
            int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();

            // 先按 PID 分组，只收集 File 类型句柄，避免重复 OpenProcess
            var perProcessHandles = new Dictionary<int, List<nint>>();
            for (long i = 0; i < handleCount; i++)
            {
                nint entryPtr = nint.Add(buffer, entryOffset + (int)(i * entrySize));
                var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(entryPtr);

                if (entry.ObjectTypeIndex != fileObjectTypeIndex.Value)
                    continue;

                int pid = (int)entry.UniqueProcessId;
                if (pid <= 4 || pid == currentPid)
                    continue;

                if (!perProcessHandles.TryGetValue(pid, out var list))
                {
                    list = [];
                    perProcessHandles[pid] = list;
                }
                list.Add((nint)entry.HandleValue);
            }

            nint currentProcHandle = Process.GetCurrentProcess().Handle;
            string dirPrefix = isDir ? EnsureTrailingSlash(targetPath) : string.Empty;
            var pathBuf = new StringBuilder(1024);

            foreach (var (pid, handles) in perProcessHandles)
            {
                nint hProc = OpenProcess(ProcessDupHandle | ProcessQueryLimitedInformation, false, pid);
                if (hProc == 0) continue;

                try
                {
                    foreach (var remoteHandle in handles)
                    {
                        if (!DuplicateHandle(hProc, remoteHandle, currentProcHandle, out nint dupHandle, 0, false, DuplicateSameAccess))
                            continue;

                        try
                        {
                            // 极其关键：仅对磁盘文件/目录 (FILE_TYPE_DISK) 查询路径，绝不触碰命名管道防死锁
                            if (GetFileType(dupHandle) != FileTypeDisk)
                                continue;

                            pathBuf.Clear();
                            uint len = GetFinalPathNameByHandleW(dupHandle, pathBuf, (uint)pathBuf.Capacity, 0);
                            if (len == 0 || len >= pathBuf.Capacity) continue;

                            string resolved = StripExtendedPathPrefix(pathBuf.ToString());
                            bool matched = isDir
                                ? resolved.Equals(targetPath, StringComparison.OrdinalIgnoreCase) ||
                                  resolved.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase)
                                : resolved.Equals(targetPath, StringComparison.OrdinalIgnoreCase);

                            if (!matched) continue;

                            if (!map.TryGetValue(pid, out var existing))
                            {
                                string pName = TryGetProcessName(pid) ?? $"PID {pid}";
                                existing = new FileLockInfo
                                {
                                    ProcessId = pid,
                                    ProcessName = pName,
                                    ProcessPath = TryGetProcessPath(pid),
                                    LockedTargetPath = resolved,
                                    Method = LockDetectMethod.OpenDiskHandle,
                                    IsCriticalSystemProcess = CriticalProcesses.Contains(pName)
                                };
                                map[pid] = existing;
                            }

                            if (!existing.HandleValues.Contains(remoteHandle))
                                existing.HandleValues.Add(remoteHandle);
                        }
                        finally
                        {
                            CloseHandle(dupHandle);
                        }
                    }
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 动态探测当前 Windows 版本的 File 内核对象类型编号（避免在不同 Win10/Win11 版本硬编码失效）
    /// </summary>
    private static ushort? DetectFileObjectTypeIndex()
    {
        try
        {
            string probePath = Environment.ProcessPath ?? Path.Combine(Environment.SystemDirectory, "notepad.exe");
            using var fs = new FileStream(probePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            nint myHandle = fs.SafeFileHandle.DangerousGetHandle();
            int myPid = Environment.ProcessId;

            uint bufferSize = 1024 * 1024 * 2;
            nint buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                uint status;
                while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out uint needed)) == StatusInfoLengthMismatch)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = Math.Max(needed + 65536u, bufferSize * 2);
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                }
                if (status != 0) return null;

                long count = Marshal.ReadIntPtr(buffer).ToInt64();
                int entryOffset = nint.Size * 2;
                int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();

                for (long i = 0; i < count; i++)
                {
                    nint ptr = nint.Add(buffer, entryOffset + (int)(i * entrySize));
                    var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(ptr);
                    if ((int)entry.UniqueProcessId == myPid && (nint)entry.HandleValue == myHandle)
                    {
                        return entry.ObjectTypeIndex;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static string StripExtendedPathPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return path[4..];
        return path;
    }

    private static string EnsureTrailingSlash(string path)
        => path.EndsWith('\\') ? path : path + "\\";

    private static void ClearAttributesRecursive(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(sub, FileAttributes.Normal); } catch { }
        }
        try { File.SetAttributes(dir, FileAttributes.Normal); } catch { }
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetProcessPath(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void EnableDebugPrivilege()
    {
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0028, out nint hToken))
                return;
            try
            {
                if (LookupPrivilegeValueW(null, "SeDebugPrivilege", out LUID luid))
                {
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x02 };
                    AdjustTokenPrivileges(hToken, false, ref tp, 0, 0, 0);
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        catch
        {
            // ignore
        }
    }
}
