using System.IO;
using System.Runtime.InteropServices;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 6：通过 NVIDIA 官方驱动底层接口 (nvapi64.dll DRS) 将「着色器缓存大小」扩充至「无限制」
/// 同时提供安全清理损坏旧着色器缓存的静态工具方法。
/// </summary>
public sealed class NvidiaUnlimitedShaderCacheTweak : ITweakModule
{
    private const uint ShaderCacheSizeSettingId = 0x00AC8497u; // PS_SHADERDISKCACHE_MAX_SIZE_ID
    private const uint UnlimitedCacheValue = 0xFFFFFFFFu;      // NVIDIA 控制面板「无限制」枚举值
    private const int NvDrsSettingStructSize = 12320;
    private const uint NvDrsSettingVer1 = (1u << 16) | (uint)NvDrsSettingStructSize;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NvQueryInterfaceDelegate(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvCreateSessionDelegate(out nint hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvSessionOpDelegate(nint hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvGetBaseProfileDelegate(nint hSession, out nint hProfile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvProfileSettingBufDelegate(nint hSession, nint hProfile, nint pSetting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvGetSettingDelegate(nint hSession, nint hProfile, uint settingId, nint pSetting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvDeleteSettingDelegate(nint hSession, nint hProfile, uint settingId);

    public string Id => "nvidia_unlimited_shader_cache";
    public string Title => "NVIDIA 着色器缓存扩容至「无限制」";
    public string Description => "通过 nvapi64.dll 官方驱动配置接口直接将 NVIDIA 控制面板「着色器缓存大小」设为无限制，消除《永劫无间》初见皮肤/奥义特效时的实时编译卡顿。";
    public string TechnicalDetail => "NVAPI DRS Global Profile (PS_SHADERDISKCACHE_MAX_SIZE 0x00AC8497 = 0xFFFFFFFF 无限制)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE7F4";
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var (supported, isSet, currentMb) = QueryNvidiaShaderCacheSetting();
            if (!supported)
                return new TweakStatus(false, "未检测到 NVIDIA 驱动");

            if (!isSet)
                return new TweakStatus(false, "驱动默认值 (约 4GB)");

            if (currentMb == UnlimitedCacheValue)
                return new TweakStatus(true, "无限制（已开启）");

            if (currentMb >= 102400u)
                return new TweakStatus(true, $"已扩容 ({currentMb / 1024} GB)");

            if (currentMb == 0u)
                return new TweakStatus(false, "已禁用（易卡顿）");

            return new TweakStatus(false, currentMb >= 1024 ? $"当前：{currentMb / 1024} GB" : $"当前：{currentMb} MB");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            log("正在连接 NVIDIA 驱动配置引擎 (nvapi64.dll DRS)...");
            var (ok, msg) = WriteNvidiaShaderCacheUnlimited(revertToDefault: false);
            return new TweakResult(ok, msg);
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            log("正在将 NVIDIA 控制面板「着色器缓存大小」重置回驱动程序默认值...");
            var (ok, msg) = WriteNvidiaShaderCacheUnlimited(revertToDefault: true);
            return new TweakResult(ok, msg);
        });
    }

    public static async Task<(int DeletedFiles, double FreedMb)> CleanStaleShaderCachesAsync(Action<string> log)
    {
        return await Task.Run(() =>
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            string[] cacheDirs =
            [
                Path.Combine(localAppData, "NVIDIA", "DXCache"),
                Path.Combine(localAppData, "NVIDIA", "GLCache"),
                Path.Combine(localAppData, "D3DSCache"),
                Path.Combine(programData, "NVIDIA Corporation", "NV_Cache"),
                Path.Combine(localAppData, "AMD", "DxCache")
            ];

            int deletedCount = 0;
            long freedBytes = 0;

            foreach (var dir in cacheDirs)
            {
                if (!Directory.Exists(dir)) continue;
                log($"正在扫描着色器缓存目录：{dir}...");

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            long len = fi.Length;
                            fi.Delete();
                            deletedCount++;
                            freedBytes += len;
                        }
                        catch
                        {
                            // 正在被当前桌面/驱动占用的活跃缓存文件自动跳过，绝对安全不报错
                        }
                    }
                }
                catch
                {
                    // ignore inaccessible subdirectories
                }
            }

            return (deletedCount, freedBytes / 1048576.0);
        });
    }

    private static (bool Supported, bool IsCustomSet, uint Value) QueryNvidiaShaderCacheSetting()
    {
        nint mod = LoadLibraryW("nvapi64.dll");
        if (mod == 0) return (false, false, 0);

        nint qiPtr = GetProcAddress(mod, "nvapi_QueryInterface");
        if (qiPtr == 0) return (false, false, 0);

        var qi = Marshal.GetDelegateForFunctionPointer<NvQueryInterfaceDelegate>(qiPtr);
        var init = GetFunc<NvInitDelegate>(qi, 0x0150E828);
        var create = GetFunc<NvCreateSessionDelegate>(qi, 0x0694D52E);
        var load = GetFunc<NvSessionOpDelegate>(qi, 0x375DBD6B);
        var getBase = GetFunc<NvGetBaseProfileDelegate>(qi, 0xDA8466A0);
        var getSetting = GetFunc<NvGetSettingDelegate>(qi, 0x73BF8338);
        var destroy = GetFunc<NvSessionOpDelegate>(qi, 0xDAD9CFF8);

        if (init == null || create == null || load == null || getBase == null || getSetting == null || destroy == null)
            return (false, false, 0);

        if (init() != 0) return (false, false, 0);
        if (create(out nint session) != 0) return (false, false, 0);

        nint buf = Marshal.AllocHGlobal(NvDrsSettingStructSize);
        try
        {
            ZeroMemory(buf, NvDrsSettingStructSize);
            if (load(session) != 0 || getBase(session, out nint profile) != 0)
                return (true, false, 0);

            Marshal.WriteInt32(buf, 0, unchecked((int)NvDrsSettingVer1));
            int status = getSetting(session, profile, ShaderCacheSizeSettingId, buf);
            if (status == 0)
            {
                // Offset 8220 = u32CurrentValue inside NVDRS_SETTING_V1
                uint val = unchecked((uint)Marshal.ReadInt32(buf, 8220));
                return (true, true, val);
            }

            // -160 = NVAPI_SETTING_NOT_FOUND (using driver default)
            return (true, false, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            destroy(session);
        }
    }

    private static (bool Success, string Message) WriteNvidiaShaderCacheUnlimited(bool revertToDefault)
    {
        nint mod = LoadLibraryW("nvapi64.dll");
        if (mod == 0)
            return (false, "未检测到 NVIDIA 显卡驱动组件 (nvapi64.dll)。");

        nint qiPtr = GetProcAddress(mod, "nvapi_QueryInterface");
        if (qiPtr == 0)
            return (false, "无法解析 nvapi_QueryInterface 接口。");

        var qi = Marshal.GetDelegateForFunctionPointer<NvQueryInterfaceDelegate>(qiPtr);
        var init = GetFunc<NvInitDelegate>(qi, 0x0150E828);
        var create = GetFunc<NvCreateSessionDelegate>(qi, 0x0694D52E);
        var load = GetFunc<NvSessionOpDelegate>(qi, 0x375DBD6B);
        var getBase = GetFunc<NvGetBaseProfileDelegate>(qi, 0xDA8466A0);
        var setSetting = GetFunc<NvProfileSettingBufDelegate>(qi, 0x577DD202);
        var delSetting = GetFunc<NvDeleteSettingDelegate>(qi, 0xE4A26362);
        var save = GetFunc<NvSessionOpDelegate>(qi, 0xFCBC7E14);
        var destroy = GetFunc<NvSessionOpDelegate>(qi, 0xDAD9CFF8);

        if (init == null || create == null || load == null || getBase == null || setSetting == null || save == null || destroy == null)
            return (false, "当前显卡驱动版本不支持完整的 NVAPI DRS 接口。");

        int initRet = init();
        if (initRet != 0)
            return (false, $"NVAPI 初始化返回错误码：{initRet}");

        if (create(out nint session) != 0)
            return (false, "无法创建 NVIDIA DRS 配置会话。");

        nint buf = Marshal.AllocHGlobal(NvDrsSettingStructSize);
        try
        {
            if (load(session) != 0 || getBase(session, out nint profile) != 0)
                return (false, "加载 NVIDIA 全局 3D 配置文件失败。");

            if (revertToDefault)
            {
                delSetting?.Invoke(session, profile, ShaderCacheSizeSettingId);
                int saveRet = save(session);
                return saveRet == 0
                    ? (true, "已将 NVIDIA 控制面板「着色器缓存大小」恢复为驱动程序默认值。")
                    : (false, $"保存 NVIDIA 驱动配置失败（错误码：{saveRet}）。");
            }
            else
            {
                ZeroMemory(buf, NvDrsSettingStructSize);
                // NVDRS_SETTING_V1 exact layout:
                // [0]    version = NvDrsSettingVer1
                // [4100] settingId = 0x00AC8497
                // [4104] settingType = 0 (NVDRS_DWORD_TYPE)
                // [8220] u32CurrentValue = 0xFFFFFFFF (Unlimited)
                Marshal.WriteInt32(buf, 0, unchecked((int)NvDrsSettingVer1));
                Marshal.WriteInt32(buf, 4100, unchecked((int)ShaderCacheSizeSettingId));
                Marshal.WriteInt32(buf, 4104, 0);
                Marshal.WriteInt32(buf, 8220, unchecked((int)UnlimitedCacheValue));

                int setRet = setSetting(session, profile, buf);
                if (setRet != 0)
                    return (false, $"写入着色器缓存无限制失败（错误码：{setRet}）。");

                int saveRet = save(session);
                return saveRet == 0
                    ? (true, "已将 NVIDIA 控制面板「着色器缓存大小」成功设为「无限制」（打开 NVIDIA 控制面板即可看到已同步生效）！")
                    : (false, $"提交 NVIDIA 驱动配置失败（错误码：{saveRet}）。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            destroy(session);
        }
    }

    private static T? GetFunc<T>(NvQueryInterfaceDelegate qi, uint id) where T : Delegate
    {
        nint ptr = qi(id);
        return ptr == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private static void ZeroMemory(nint ptr, int size)
    {
        for (int i = 0; i < size; i += 8)
        {
            if (i + 8 <= size)
                Marshal.WriteInt64(ptr, i, 0L);
            else
                Marshal.WriteInt32(ptr, i, 0);
        }
    }
}
