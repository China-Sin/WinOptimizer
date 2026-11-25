using System.IO;
using Microsoft.Win32;
using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 补充 2：去除桌面图标右下角「蓝黄小盾牌」与新建快捷方式「 - 快捷方式」后缀
/// 自动在 System32 生成标准 32x32 全透明 Blank.ico 供新装机环境直接使用。
/// </summary>
public sealed class RemoveShortcutSuffixAndShieldTweak : ITweakModule
{
    private const string ShellIconsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    private const string UserExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private static readonly string BlankIcoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Blank.ico");

    public string Id => "remove_shield_overlay_and_shortcut_suffix";
    public string Title => "去除图标「蓝黄小盾牌」与「 - 快捷方式」字样";
    public string Description => "自动配置全透明图标隐藏软件图标右下角的蓝黄 UAC 盾牌角标，并禁止创建快捷方式时自动追加「 - 快捷方式」尾巴。";
    public string TechnicalDetail => @"Shell Icons (77/179 -> Blank.ico) + HKCU\...\Explorer (link = 00 00 00 00)";
    public string Category => "桌面与体验";
    public string IconGlyph => "\uE790";
    public bool NeedsExplorerRestart => true;

    public Task<TweakStatus> CheckStatusAsync()
    {
        return Task.Run(() =>
        {
            var icon77 = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, ShellIconsPath, "77")?.ToString();
            var linkVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, UserExplorerPath, "link");

            bool shieldRemoved = !string.IsNullOrWhiteSpace(icon77) && File.Exists(icon77.Split(',')[0].Trim('"'));
            bool suffixRemoved = linkVal is byte[] bytes && bytes.Length >= 4 && bytes.All(b => b == 0);

            if (shieldRemoved && suffixRemoved)
                return new TweakStatus(true, "已去盾牌与后缀");
            if (shieldRemoved || suffixRemoved)
                return new TweakStatus(false, shieldRemoved ? "仅去小盾牌" : "仅去快捷方式后缀");

            return new TweakStatus(false, "系统默认（含盾牌与后缀）");
        });
    }

    public async Task<TweakResult> ApplyAsync(Action<string> log)
    {
        try
        {
            if (!File.Exists(BlankIcoPath))
            {
                log($"正在生成全透明图标文件 {BlankIcoPath}...");
                EnsureTransparentBlankIcoExists(BlankIcoPath);
            }

            log("正在配置 Shell Icons (77 / 179) 隐藏图标蓝黄小盾牌...");
            SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ShellIconsPath, "77", BlankIcoPath);
            SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ShellIconsPath, "179", BlankIcoPath);

            log("正在设置 Explorer\\link = 00 00 00 00 去除新建快捷方式后缀...");
            SystemHelper.SetRegistryBinary(RegistryHive.CurrentUser, UserExplorerPath, "link", [0, 0, 0, 0]);

            log("正在刷新系统图标缓存...");
            await SystemHelper.RunPowerShellAsync("ie4uinit.exe -show");

            return new TweakResult(true, "已去除图标小盾牌与快捷方式后缀（重启资源管理器后桌面图标彻底刷新）。", RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"设置失败：{ex.Message}");
        }
    }

    public async Task<TweakResult> RevertAsync(Action<string> log)
    {
        try
        {
            log("正在恢复默认 UAC 小盾牌角标与快捷方式命名规则...");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, ShellIconsPath, "77");
            SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, ShellIconsPath, "179");
            SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, UserExplorerPath, "link");

            await SystemHelper.RunPowerShellAsync("ie4uinit.exe -show");
            return new TweakResult(true, "已恢复系统默认图标盾牌与快捷方式后缀。", RecommendExplorerRestart: true);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"恢复失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 生成标准 32x32 32-bit ARGB 全透明原生 ICO 文件（无外部图形库依赖）
    /// </summary>
    private static void EnsureTransparentBlankIcoExists(string path)
    {
        const int width = 32;
        const int height = 32;
        const int xorBytes = width * height * 4;
        const int andBytes = (width / 8) * height;
        const int imageSize = 40 + xorBytes + andBytes;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // ICONDIR (6 bytes)
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type = 1 (Icon)
        bw.Write((ushort)1); // Count = 1

        // ICONDIRENTRY (16 bytes)
        bw.Write((byte)width);
        bw.Write((byte)height);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);  // Planes
        bw.Write((ushort)32); // BitCount
        bw.Write((uint)imageSize);
        bw.Write((uint)22);   // Offset

        // BITMAPINFOHEADER (40 bytes)
        bw.Write((uint)40);
        bw.Write((int)width);
        bw.Write((int)(height * 2)); // XOR + AND height
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)0); // BI_RGB
        bw.Write((uint)(xorBytes + andBytes));
        bw.Write(0);
        bw.Write(0);
        bw.Write(0u);
        bw.Write(0u);

        // XOR mask: 32x32 transparent BGRA = all 0x00
        bw.Write(new byte[xorBytes]);

        // AND mask: all 1s (0xFF) = transparent mask fallback
        var andMask = new byte[andBytes];
        Array.Fill(andMask, (byte)0xFF);
        bw.Write(andMask);
    }
}
