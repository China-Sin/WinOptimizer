using System.IO;
using Microsoft.Win32;

namespace WinOptimizer.Core;

public record EnvCheckStatus(
    bool IsInstalled,
    string VersionText
);

/// <summary>
/// 开发环境与常用工具安装包描述模型。
/// 后续想要增加任何软件或运行环境，只需在 EnvRegistry 中新增一条配置即可！
/// </summary>
public sealed class EnvPackageDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string WingetId { get; init; }
    public required string IconGlyph { get; init; }
    public string? CheckCommand { get; init; }
    public string[] FallbackPaths { get; init; } = [];
    public string? RegistryKeyword { get; init; }
    public bool IsRecommendedPreset { get; init; }

    public async Task<EnvCheckStatus> CheckInstalledAsync()
    {
        // 1. 优先通过命令行版本输出检测（如 python --version / node -v）
        if (!string.IsNullOrWhiteSpace(CheckCommand))
        {
            var (code, stdout, stderr) = await SystemHelper.RunPowerShellAsync(CheckCommand, timeoutSeconds: 12);
            var combined = !string.IsNullOrWhiteSpace(stdout) ? stdout : stderr;
            if (code == 0 && !string.IsNullOrWhiteSpace(combined))
            {
                var firstLine = combined
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim() ?? "已安装";

                // 排除 Windows Store 空壳 python.exe 假路径提示
                if (!firstLine.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase) &&
                    !firstLine.Contains("was not found", StringComparison.OrdinalIgnoreCase) &&
                    !firstLine.Contains("找不到", StringComparison.OrdinalIgnoreCase))
                {
                    if (firstLine.Length > 34)
                        firstLine = firstLine[..34] + "...";
                    return new EnvCheckStatus(true, firstLine);
                }
            }
        }

        // 2. 检查常见默认安装路径兜底（防止刚安装完环境变量特殊路径未刷新）
        foreach (var rawPath in FallbackPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(rawPath);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                return new EnvCheckStatus(true, "已安装");
            }
        }

        // 3. 注册表卸载列表检测（适用于 VC++ 运行库等无 CLI 命令的组件）
        if (!string.IsNullOrWhiteSpace(RegistryKeyword))
        {
            var matchedVersion = FindUninstallDisplayVersion(RegistryKeyword);
            if (matchedVersion != null)
            {
                return new EnvCheckStatus(true, string.IsNullOrWhiteSpace(matchedVersion) ? "已安装" : $"v{matchedVersion}");
            }
        }

        return new EnvCheckStatus(false, "未安装");
    }

    private static string? FindUninstallDisplayVersion(string keyword)
    {
        string[] uninstallRoots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var root in uninstallRoots)
            {
                try
                {
                    using var baseKey = SystemHelper.OpenBaseKey(hive);
                    using var parent = baseKey.OpenSubKey(root, writable: false);
                    if (parent == null) continue;

                    foreach (var subName in parent.GetSubKeyNames())
                    {
                        using var sub = parent.OpenSubKey(subName, writable: false);
                        var displayName = sub?.GetValue("DisplayName")?.ToString();
                        if (!string.IsNullOrWhiteSpace(displayName) &&
                            displayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            return sub?.GetValue("DisplayVersion")?.ToString() ?? "";
                        }
                    }
                }
                catch
                {
                    // ignore inaccessible keys
                }
            }
        }

        return null;
    }
}
