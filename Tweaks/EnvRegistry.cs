using WinOptimizer.Core;

namespace WinOptimizer.Tweaks;

/// <summary>
/// 开发环境与装机必备工具注册表。
/// 使用微软官方 winget 源静默安装并自动写入系统 PATH。
/// 后续扩展新软件只需在此处追加一个 EnvPackageDefinition 对象。
/// </summary>
public static class EnvRegistry
{
    public static IReadOnlyList<EnvPackageDefinition> CreateAllPackages() =>
    [
        new EnvPackageDefinition
        {
            Id = "env_python312",
            Name = "Python 3.12（含 pip & 自动配置 PATH）",
            Category = "语言与运行环境",
            Description = "主流 Python 3.12 官方稳定版，自动添加至全局系统环境变量 PATH 并附带 pip 包管理器。",
            WingetId = "Python.Python.3.12",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command python -ErrorAction SilentlyContinue) { python --version }",
            IsRecommendedPreset = true
        },
        new EnvPackageDefinition
        {
            Id = "env_nodejs_lts",
            Name = "Node.js（长期支持稳定版 + npm）",
            Category = "语言与运行环境",
            Description = "前端与全栈必备 JavaScript 运行时环境（OpenJS 官方源），自带 npm 与 corepack。",
            WingetId = "OpenJS.NodeJS.LTS",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command node -ErrorAction SilentlyContinue) { 'Node ' + (node --version) }",
            FallbackPaths = [@"%ProgramFiles%\nodejs\node.exe"],
            IsRecommendedPreset = true
        },
        new EnvPackageDefinition
        {
            Id = "env_git",
            Name = "Git for Windows",
            Category = "必备开发工具",
            Description = "分布式代码版本管理工具，包含 Git Bash、Git CMD 与 SSH 客户端，拉取 GitHub 项目必备。",
            WingetId = "Git.Git",
            IconGlyph = "\uE8AD",
            CheckCommand = "if (Get-Command git -ErrorAction SilentlyContinue) { (git --version) -replace 'git version ','Git ' }",
            FallbackPaths = [@"%ProgramFiles%\Git\cmd\git.exe"],
            IsRecommendedPreset = true
        },
        new EnvPackageDefinition
        {
            Id = "env_vcredist",
            Name = "Visual C++ 2015-2022 运行库 (x64)",
            Category = "语言与运行环境",
            Description = "Windows 装机必备微软官方 C++ 运行库，解决 Python 扩展、游戏及各类软件缺失 VCRUNTIME140.dll 报错。",
            WingetId = "Microsoft.VCRedist.2015+.x64",
            IconGlyph = "\uE74C",
            RegistryKeyword = "Microsoft Visual C++ 2015-2022 Redistributable (x64)",
            IsRecommendedPreset = true
        },
        new EnvPackageDefinition
        {
            Id = "env_uv",
            Name = "uv（现代极速 Python 环境/包管理器）",
            Category = "必备开发工具",
            Description = "比 pip 快 10~100 倍的新一代 Python 虚拟环境与多版本管理工具（Rust 编写，可随时切换任意 Python 版本）。",
            WingetId = "astral-sh.uv",
            IconGlyph = "\uE945",
            CheckCommand = "if (Get-Command uv -ErrorAction SilentlyContinue) { uv --version }",
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_golang",
            Name = "Go (Golang) 开发环境",
            Category = "语言与运行环境",
            Description = "Google 官方 Go 编程语言编译器与标准工具链，自动配置 GOROOT 与系统 PATH。",
            WingetId = "GoLang.Go",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command go -ErrorAction SilentlyContinue) { (go version).Split(' ')[2] }",
            FallbackPaths = [@"C:\Program Files\Go\bin\go.exe"],
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_rustup",
            Name = "Rust (rustup 工具链管理器)",
            Category = "语言与运行环境",
            Description = "官方 Rust 语言安装与版本管理工具（包含 cargo 与 rustc）。",
            WingetId = "Rustlang.Rustup",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command rustc -ErrorAction SilentlyContinue) { (rustc --version).Split(' ')[0..1] -join ' ' }",
            FallbackPaths = [@"%USERPROFILE%\.cargo\bin\rustup.exe"],
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_jdk21",
            Name = "Java JDK 21 LTS (Eclipse Temurin)",
            Category = "语言与运行环境",
            Description = "开源免费的企业级 Java 21 长期支持版开发套件（Adoptium 官方构建）。",
            WingetId = "EclipseAdoptium.Temurin.21.JDK",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command java -ErrorAction SilentlyContinue) { (java --version | Select-Object -First 1) }",
            RegistryKeyword = "Temurin 21",
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_dotnet8",
            Name = ".NET 8.0 SDK",
            Category = "语言与运行环境",
            Description = "微软官方跨平台 C# / .NET 8 长期支持版软件开发工具包与运行时。",
            WingetId = "Microsoft.DotNet.SDK.8",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command dotnet -ErrorAction SilentlyContinue) { '.NET ' + (dotnet --version) }",
            FallbackPaths = [@"C:\Program Files\dotnet\dotnet.exe"],
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_vscode",
            Name = "Visual Studio Code",
            Category = "必备开发工具",
            Description = "微软主流轻量级全能代码编辑器，支持 Python / Node.js / C# / Go 等丰富扩展插件。",
            WingetId = "Microsoft.VisualStudioCode",
            IconGlyph = "\uE943",
            CheckCommand = "if (Get-Command code -ErrorAction SilentlyContinue) { 'VS Code ' + (code --version | Select-Object -First 1) }",
            RegistryKeyword = "Microsoft Visual Studio Code",
            IsRecommendedPreset = false
        },
        new EnvPackageDefinition
        {
            Id = "env_7zip",
            Name = "7-Zip 解压缩工具",
            Category = "必备开发工具",
            Description = "轻量、开源、零广告的高压缩比解压工具（支持 7z / zip / rar / tar / iso）。",
            WingetId = "7zip.7zip",
            IconGlyph = "\uF012",
            FallbackPaths = [@"C:\Program Files\7-Zip\7zFM.exe"],
            RegistryKeyword = "7-Zip",
            IsRecommendedPreset = true
        },
        new EnvPackageDefinition
        {
            Id = "env_windhawk",
            Name = "Windhawk（Windows 11 开始菜单/任务栏深度美化神器）",
            Category = "系统增强工具",
            Description = "轻量级模块化定制工具，安装其中的 Start Menu Styler 插件可任意隐藏新版开始菜单「全部」栏、透明化或自定义宽度。",
            WingetId = "RamenSoftware.Windhawk",
            IconGlyph = "\uE790",
            FallbackPaths = [@"C:\Program Files\Windhawk\windhawk.exe"],
            RegistryKeyword = "Windhawk",
            IsRecommendedPreset = false
        }
    ];

    public static async Task<TweakResult> InstallOrUpgradeAsync(EnvPackageDefinition pkg, bool isUpgrade, Action<string> log)
    {
        var checkWinget = await SystemHelper.RunPowerShellAsync("if (Get-Command winget -ErrorAction SilentlyContinue) { 'OK' } else { 'MISSING' }");
        if (!checkWinget.Output.Contains("OK"))
        {
            return new TweakResult(false, "未检测到系统自带的 winget (App Installer)，请先更新微软商店中的「应用安装程序」。");
        }

        string verb = isUpgrade ? "upgrade" : "install";
        string actionTitle = isUpgrade ? "升级" : "安装";

        log($"开始通过 winget {actionTitle} [{pkg.Name}] (ID: {pkg.WingetId})，请耐心等待下载与部署...");

        // 对于 Python 额外传入 PrependPath=1 确保所有用户/当前用户自动加入 PATH
        string extraOverride = pkg.WingetId.StartsWith("Python.Python", StringComparison.OrdinalIgnoreCase) && !isUpgrade
            ? " --override \"/quiet InstallAllUsers=1 PrependPath=1 Include_test=0\""
            : "";

        string cmd = $"winget {verb} --id \"{pkg.WingetId}\" --exact --silent --accept-package-agreements --accept-source-agreements{extraOverride}";
        var (exitCode, output, err) = await SystemHelper.RunPowerShellAsync(cmd, log, timeoutSeconds: 600);

        RefreshProcessEnvironmentPath();

        // winget 中 -1978335189 (0x8A15002B) 代表已是最新版本无需更新
        if (exitCode == 0 || output.Contains("No applicable upgrade", StringComparison.OrdinalIgnoreCase) || output.Contains("找不到可用的升级"))
        {
            return new TweakResult(true, $"[{pkg.Name}] {actionTitle}完成！");
        }

        var statusAfter = await pkg.CheckInstalledAsync();
        if (statusAfter.IsInstalled && !isUpgrade)
        {
            return new TweakResult(true, $"[{pkg.Name}] 已成功安装 ({statusAfter.VersionText})。");
        }

        return new TweakResult(false, $"[{pkg.Name}] {actionTitle}返回代码 {exitCode}。{err}".Trim());
    }

    public static async Task<TweakResult> UninstallAsync(EnvPackageDefinition pkg, Action<string> log)
    {
        log($"正在卸载 [{pkg.Name}] (ID: {pkg.WingetId})...");
        string cmd = $"winget uninstall --id \"{pkg.WingetId}\" --exact --silent --accept-source-agreements";
        var (exitCode, _, err) = await SystemHelper.RunPowerShellAsync(cmd, log, timeoutSeconds: 300);

        RefreshProcessEnvironmentPath();

        if (exitCode == 0)
            return new TweakResult(true, $"[{pkg.Name}] 已卸载完成。");

        return new TweakResult(false, $"[{pkg.Name}] 卸载返回代码 {exitCode}。{err}".Trim());
    }

    public static void RefreshProcessEnvironmentPath()
    {
        try
        {
            var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            Environment.SetEnvironmentVariable("Path", $"{machinePath};{userPath}", EnvironmentVariableTarget.Process);
        }
        catch
        {
            // ignore
        }
    }
}
