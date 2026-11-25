using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 永劫专项 1：关闭物理网卡 Nagle 攒包算法与 TCP 延迟确认 (TcpAckFrequency=1 + TCPNoDelay=1)
/// 纯协议栈级优化，不动硬件中断，100% 兼容不掉线，消除振刀/闪避指令的发包等待。
/// </summary>
public sealed class DisableNagleTcpLowLatencyTweak : ITweakModule
{
    private const string TcpInterfacesRoot = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public string Id => "disable_nagle_tcp_low_latency";
    public string Title => "网游发包零延迟：关闭网卡 Nagle 攒包算法";
    public string Description => "为活动网络接口写入 TcpAckFrequency=1 与 TCPNoDelay=1，取消 Windows 默认 40~200ms 小数据包攒包等待，改善《永劫无间》振刀/闪避慢半拍现象。";
    public string TechnicalDetail => @"HKLM\...\Tcpip\Parameters\Interfaces\{GUID} (TcpAckFrequency=1, TCPNoDelay=1, TcpDelAckTicks=0)";
    public string Category => "游戏与延迟";
    public string IconGlyph => "\uE774"; // Network / Globe icon
    public bool NeedsExplorerRestart => false;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var activeGuids = GetActiveInterfaceGuids();
            if (activeGuids.Count == 0)
                return new TweakStatus(false, "未检测到活动网卡");

            int optimized = 0;
            foreach (var guid in activeGuids)
            {
                string path = $@"{TcpInterfacesRoot}\{guid}";
                var ack = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, path, "TcpAckFrequency");
                var noDelay = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, path, "TCPNoDelay");
                if (ack is int a && a == 1 && noDelay is int n && n == 1)
                    optimized++;
            }

            if (optimized == activeGuids.Count)
                return new TweakStatus(true, $"已开启低延迟发包 ({optimized} 网卡)");
            if (optimized > 0)
                return new TweakStatus(false, $"部分网卡生效 ({optimized}/{activeGuids.Count})");

            return new TweakStatus(false, "系统默认（Nagle 合并发包）");
        });
    }

    public Task<TweakResult> ApplyAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                var activeGuids = GetActiveInterfaceGuids();
                if (activeGuids.Count == 0)
                    return new TweakResult(false, "未找到具有有效 IPv4 地址的活动网卡接口。");

                foreach (var guid in activeGuids)
                {
                    string path = $@"{TcpInterfacesRoot}\{guid}";
                    log($"正在为活动网卡接口 {guid} 写入 TcpAckFrequency=1, TCPNoDelay=1...");
                    SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, path, "TcpAckFrequency", 1);
                    SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, path, "TCPNoDelay", 1);
                    SystemHelper.SetRegistryDword(RegistryHive.LocalMachine, path, "TcpDelAckTicks", 0);
                }

                return new TweakResult(true, $"已为 {activeGuids.Count} 个活动网卡关闭 Nagle 攒包算法（重连网络或重启后完全生效）。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"优化网卡 TCP 参数失败：{ex.Message}");
            }
        });
    }

    public Task<TweakResult> RevertAsync(Action<string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                using var hklm = SystemHelper.OpenBaseKey(RegistryHive.LocalMachine);
                using var root = hklm.OpenSubKey(TcpInterfacesRoot, writable: false);
                if (root != null)
                {
                    foreach (var guid in root.GetSubKeyNames())
                    {
                        string path = $@"{TcpInterfacesRoot}\{guid}";
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, path, "TcpAckFrequency");
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, path, "TCPNoDelay");
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, path, "TcpDelAckTicks");
                    }
                }
                log("已清除所有网卡自定义 TCP 发包参数，恢复 Windows 默认 Nagle 策略。");
                return new TweakResult(true, "已恢复系统默认网络 TCP 协议栈发包设置。");
            }
            catch (Exception ex)
            {
                return new TweakResult(false, $"恢复失败：{ex.Message}");
            }
        });
    }

    private static List<string> GetActiveInterfaceGuids()
    {
        var list = new List<string>();
        try
        {
            using var hklm = SystemHelper.OpenBaseKey(RegistryHive.LocalMachine);
            using var root = hklm.OpenSubKey(TcpInterfacesRoot, writable: false);
            if (root == null) return list;

            foreach (var subName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subName, writable: false);
                if (sub == null) continue;

                string? dhcpIp = sub.GetValue("DhcpIPAddress")?.ToString();
                string? staticIp = sub.GetValue("IPAddress") switch
                {
                    string[] arr => arr.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "0.0.0.0"),
                    string s => s,
                    _ => null
                };

                bool hasValidIp = (!string.IsNullOrWhiteSpace(dhcpIp) && dhcpIp != "0.0.0.0") ||
                                  (!string.IsNullOrWhiteSpace(staticIp) && staticIp != "0.0.0.0");
                if (hasValidIp)
                    list.Add(subName);
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }
}
