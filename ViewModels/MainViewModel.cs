using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using WinOptimizer.Core;
using WinOptimizer.Tweaks;

namespace WinOptimizer.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly List<TweakItemViewModel> _allTweaks;
    private readonly List<EnvItemViewModel> _allEnvs;
    private readonly StringBuilder _logBuilder = new();

    private string _selectedNavKey = "ALL_TWEAKS";
    private string _statusFilter = "ALL"; // ALL | OPTIMIZED | UNOPTIMIZED
    private string _searchQuery = string.Empty;
    private bool _isGlobalBusy;
    private bool _showExplorerRestartBanner;
    private string _logText = string.Empty;
    private string _pageHeaderTitle = "全部系统与游戏优化";
    private string _pageHeaderSubtitle = "实时检测系统当前状态，支持单项开关切换与一键批量优化 / 恢复默认";

    public ObservableCollection<TweakItemViewModel> FilteredTweaks { get; } = [];
    public ObservableCollection<EnvItemViewModel> FilteredEnvs { get; } = [];

    public string OsSummary { get; }
    public bool IsAdmin { get; }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand SetStatusFilterCommand { get; }
    public RelayCommand ToggleSingleTweakCommand { get; }
    public RelayCommand SelectUnoptimizedTweaksCommand { get; }
    public RelayCommand SelectAllTweaksCommand { get; }
    public RelayCommand ApplySelectedTweaksCommand { get; }
    public RelayCommand RevertSelectedTweaksCommand { get; }
    public RelayCommand RefreshAllCommand { get; }
    public RelayCommand RestartExplorerCommand { get; }
    public RelayCommand PurgeStandbyRamCommand { get; }
    public RelayCommand CleanShaderCacheCommand { get; }

    public RelayCommand InstallOrUpgradeSingleEnvCommand { get; }
    public RelayCommand UninstallSingleEnvCommand { get; }
    public RelayCommand SelectRecommendedEnvsCommand { get; }
    public RelayCommand InstallSelectedEnvsCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public MainViewModel()
    {
        OsSummary = SystemHelper.GetWindowsVersionSummary();
        IsAdmin = SystemHelper.IsRunningAsAdministrator();

        _allTweaks = TweakRegistry.CreateAllModules().Select(m => new TweakItemViewModel(m)).ToList();
        _allEnvs = EnvRegistry.CreateAllPackages().Select(p => new EnvItemViewModel(p)).ToList();

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string navKey)
            {
                SelectedNavKey = navKey;
            }
        });

        SetStatusFilterCommand = new RelayCommand(p =>
        {
            if (p is string filterKey)
            {
                StatusFilter = filterKey;
            }
        });

        ToggleSingleTweakCommand = new RelayCommand(
            async p => { if (p is TweakItemViewModel item) await ToggleSingleTweakAsync(item); },
            _ => !IsGlobalBusy);

        SelectUnoptimizedTweaksCommand = new RelayCommand(_ =>
        {
            foreach (var item in FilteredTweaks)
                item.IsSelectedForBatch = !item.IsOptimized;
            AppendLog($"已勾选当前视图下 {FilteredTweaks.Count(x => x.IsSelectedForBatch)} 项尚未开启的优化项。");
        });

        SelectAllTweaksCommand = new RelayCommand(_ =>
        {
            bool anyUnchecked = FilteredTweaks.Any(x => !x.IsSelectedForBatch);
            foreach (var item in FilteredTweaks)
                item.IsSelectedForBatch = anyUnchecked;
        });

        ApplySelectedTweaksCommand = new RelayCommand(
            async _ => await BatchExecuteTweaksAsync(applyOptimization: true),
            _ => !IsGlobalBusy);

        RevertSelectedTweaksCommand = new RelayCommand(
            async _ => await BatchExecuteTweaksAsync(applyOptimization: false),
            _ => !IsGlobalBusy);

        RefreshAllCommand = new RelayCommand(
            async _ => await RefreshCurrentViewAsync(),
            _ => !IsGlobalBusy);

        RestartExplorerCommand = new RelayCommand(
            async _ =>
            {
                IsGlobalBusy = true;
                try
                {
                    await SystemHelper.RestartExplorerAsync(AppendLog);
                    ShowExplorerRestartBanner = false;
                }
                finally
                {
                    IsGlobalBusy = false;
                }
            },
            _ => !IsGlobalBusy);

        PurgeStandbyRamCommand = new RelayCommand(
            async _ =>
            {
                IsGlobalBusy = true;
                try
                {
                    var (ok, summary) = await MemoryResidentAndStandbyPurgeTweak.PurgeStandbyListAsync(AppendLog);
                    AppendLog(ok ? $"✔ {summary}" : $"✖ {summary}");
                }
                finally
                {
                    IsGlobalBusy = false;
                }
            },
            _ => !IsGlobalBusy);

        CleanShaderCacheCommand = new RelayCommand(
            async _ =>
            {
                IsGlobalBusy = true;
                try
                {
                    AppendLog("▶ 开始清理旧版 DirectX / NVIDIA / OpenGL 着色器缓存（正被占用的活跃文件会自动安全跳过）...");
                    var (files, mb) = await NvidiaUnlimitedShaderCacheTweak.CleanStaleShaderCachesAsync(AppendLog);
                    AppendLog($"✔ 着色器缓存清理完成：共清理 {files} 个旧缓存文件，释放 {mb:F1} MB 空间。");
                }
                finally
                {
                    IsGlobalBusy = false;
                }
            },
            _ => !IsGlobalBusy);

        InstallOrUpgradeSingleEnvCommand = new RelayCommand(
            async p => { if (p is EnvItemViewModel env) await InstallSingleEnvAsync(env); },
            _ => !IsGlobalBusy);

        UninstallSingleEnvCommand = new RelayCommand(
            async p => { if (p is EnvItemViewModel env) await UninstallSingleEnvAsync(env); },
            _ => !IsGlobalBusy);

        SelectRecommendedEnvsCommand = new RelayCommand(_ =>
        {
            foreach (var env in FilteredEnvs)
                env.IsSelectedForBatch = env.IsRecommendedPreset && !env.IsInstalled;

            int count = FilteredEnvs.Count(x => x.IsSelectedForBatch);
            if (count == 0)
            {
                AppendLog("常用推荐环境（Python / Node.js / Git / VC++ / 7-Zip）在当前电脑上均已安装！");
            }
            else
            {
                AppendLog($"已自动勾选 {count} 个尚未安装的推荐装机环境。");
            }
        });

        InstallSelectedEnvsCommand = new RelayCommand(
            async _ => await BatchInstallSelectedEnvsAsync(),
            _ => !IsGlobalBusy);

        ClearLogCommand = new RelayCommand(_ =>
        {
            _logBuilder.Clear();
            LogText = string.Empty;
        });

        ApplyFilters();
        AppendLog($"系统识别：{OsSummary} | 管理员权限：{(IsAdmin ? "已获取" : "未获取")}");
    }

    public string SelectedNavKey
    {
        get => _selectedNavKey;
        set
        {
            if (SetProperty(ref _selectedNavKey, value))
            {
                OnPropertyChanged(nameof(IsTweaksView));
                OnPropertyChanged(nameof(IsEnvView));
                UpdateHeaders();
                ApplyFilters();
            }
        }
    }

    public bool IsTweaksView => !SelectedNavKey.StartsWith("ENV_", StringComparison.Ordinal);
    public bool IsEnvView => SelectedNavKey.StartsWith("ENV_", StringComparison.Ordinal);

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
                ApplyFilters();
        }
    }

    public string FilterAllTweaksLabel => $"全部 ({GetCategoryMatchingTweaks().Count()})";
    public string FilterOptimizedTweaksLabel => $"已优化 ({GetCategoryMatchingTweaks().Count(x => x.IsOptimized)})";
    public string FilterUnoptimizedTweaksLabel => $"未优化 ({GetCategoryMatchingTweaks().Count(x => !x.IsOptimized)})";

    public string FilterAllEnvsLabel => $"全部 ({_allEnvs.Count})";
    public string FilterInstalledEnvsLabel => $"已安装 ({_allEnvs.Count(x => x.IsInstalled)})";
    public string FilterUninstalledEnvsLabel => $"未安装 ({_allEnvs.Count(x => !x.IsInstalled)})";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                ApplyFilters();
        }
    }

    public bool IsGlobalBusy
    {
        get => _isGlobalBusy;
        set
        {
            if (SetProperty(ref _isGlobalBusy, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowExplorerRestartBanner
    {
        get => _showExplorerRestartBanner;
        set => SetProperty(ref _showExplorerRestartBanner, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string PageHeaderTitle
    {
        get => _pageHeaderTitle;
        private set => SetProperty(ref _pageHeaderTitle, value);
    }

    public string PageHeaderSubtitle
    {
        get => _pageHeaderSubtitle;
        private set => SetProperty(ref _pageHeaderSubtitle, value);
    }

    public int TotalTweaksCount => _allTweaks.Count;
    public int TotalEnvsCount => _allEnvs.Count;

    public async Task InitializeAsync()
    {
        AppendLog("正在并行扫描系统优化项与开发环境安装状态...");
        var tweakTasks = _allTweaks.Select(t => t.RefreshStatusAsync());
        var envTasks = _allEnvs.Select(e => e.RefreshStatusAsync());
        await Task.WhenAll(tweakTasks.Concat(envTasks));
        ApplyFilters();
        AppendLog("全部状态扫描完毕，可点击单项右侧开关切换，或勾选多项后点击顶部按钮一键执行。");
    }

    private async Task RefreshCurrentViewAsync()
    {
        IsGlobalBusy = true;
        try
        {
            EnvRegistry.RefreshProcessEnvironmentPath();
            if (IsTweaksView)
            {
                AppendLog("正在刷新系统优化项状态...");
                await Task.WhenAll(_allTweaks.Select(t => t.RefreshStatusAsync()));
            }
            else
            {
                AppendLog("正在重新检测本地开发环境版本状态...");
                await Task.WhenAll(_allEnvs.Select(e => e.RefreshStatusAsync()));
            }
            ApplyFilters();
            AppendLog("状态刷新完成。");
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private void UpdateHeaders()
    {
        (PageHeaderTitle, PageHeaderSubtitle) = SelectedNavKey switch
        {
            "ALL_TWEAKS" => ("全部系统与游戏优化", $"共 {_allTweaks.Count} 项核心调优：实时检测当前状态，支持单项开关切换与一键批量执行 / 恢复默认"),
            "CAT_GAMING" => ("游戏帧率与低延迟", "提升平均帧与 1% Low 帧，消除键鼠加速、DPC 中断延迟、网络限流与切屏黑屏"),
            "CAT_PERFORMANCE" => ("性能、电源与 SSD", "注入 GameX v2 满血卓越电源策略、清理休眠大文件并降低 SSD 无效写入"),
            "CAT_SECURITY" => ("安全与防火墙", "管理文件安全警告策略与系统网络防火墙状态"),
            "CAT_DESKTOP" => ("桌面、菜单与去遥测", "经典高效右键菜单、卸载小组件、关闭后台诊断遥测与联网搜索卡顿"),
            "CAT_UPDATE" => ("系统更新控制", "控制系统自动更新策略，一键长效暂停更新 100 年免打扰"),
            "ENV_ALL" => ("开发环境与工具安装", "基于微软官方 winget 静默部署 Python、Node.js、Git、Go、Rust 等环境并自动配置 PATH"),
            _ => ("全部系统与游戏优化", "")
        };
    }

    private IEnumerable<TweakItemViewModel> GetCategoryMatchingTweaks()
    {
        string? targetCategory = SelectedNavKey switch
        {
            "CAT_GAMING" => "游戏与延迟",
            "CAT_PERFORMANCE" => "性能与电源",
            "CAT_SECURITY" => "安全与权限",
            "CAT_DESKTOP" => "桌面与体验",
            "CAT_UPDATE" => "系统与更新",
            _ => null
        };

        return targetCategory == null
            ? _allTweaks
            : _allTweaks.Where(x => x.Category == targetCategory);
    }

    private void NotifyFilterCountsChanged()
    {
        OnPropertyChanged(nameof(FilterAllTweaksLabel));
        OnPropertyChanged(nameof(FilterOptimizedTweaksLabel));
        OnPropertyChanged(nameof(FilterUnoptimizedTweaksLabel));
        OnPropertyChanged(nameof(FilterAllEnvsLabel));
        OnPropertyChanged(nameof(FilterInstalledEnvsLabel));
        OnPropertyChanged(nameof(FilterUninstalledEnvsLabel));
    }

    private void ApplyFilters()
    {
        NotifyFilterCountsChanged();
        var q = SearchQuery.Trim();

        FilteredTweaks.Clear();
        if (IsTweaksView)
        {
            foreach (var item in GetCategoryMatchingTweaks())
            {
                if (StatusFilter == "OPTIMIZED" && !item.IsOptimized)
                    continue;
                if (StatusFilter == "UNOPTIMIZED" && item.IsOptimized)
                    continue;

                if (!string.IsNullOrEmpty(q) &&
                    !item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !item.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                FilteredTweaks.Add(item);
            }
        }

        FilteredEnvs.Clear();
        if (IsEnvView)
        {
            foreach (var env in _allEnvs)
            {
                if (StatusFilter == "OPTIMIZED" && !env.IsInstalled)
                    continue;
                if (StatusFilter == "UNOPTIMIZED" && env.IsInstalled)
                    continue;

                if (!string.IsNullOrEmpty(q) &&
                    !env.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !env.Description.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !env.WingetId.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                FilteredEnvs.Add(env);
            }
        }
    }

    private async Task ToggleSingleTweakAsync(TweakItemViewModel item)
    {
        IsGlobalBusy = true;
        item.IsBusy = true;
        try
        {
            bool targetOptimize = !item.IsOptimized;
            AppendLog($"▶ [{item.Title}] 正在{(targetOptimize ? "执行优化" : "恢复默认")}...");
            var result = targetOptimize
                ? await item.Module.ApplyAsync(AppendLog)
                : await item.Module.RevertAsync(AppendLog);

            AppendLog(result.Success ? $"✔ {result.Message}" : $"✖ {result.Message}");
            if (result.RecommendExplorerRestart)
                ShowExplorerRestartBanner = true;

            await item.RefreshStatusAsync();
            ApplyFilters();
        }
        finally
        {
            item.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private async Task BatchExecuteTweaksAsync(bool applyOptimization)
    {
        var selected = FilteredTweaks.Where(x => x.IsSelectedForBatch).ToList();
        if (selected.Count == 0)
        {
            AppendLog("提示：请先勾选至少一项功能（如果要全部优化，可先点「勾选未优化项」或「全选」）。");
            MessageBox.Show("请先在列表中勾选需要批量操作的项目！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsGlobalBusy = true;
        bool needExplorerRestart = false;
        int okCount = 0;

        try
        {
            AppendLog($"========== 开始批量{(applyOptimization ? "执行优化" : "恢复默认")}（共 {selected.Count} 项） ==========");
            foreach (var item in selected)
            {
                item.IsBusy = true;
                try
                {
                    AppendLog($"▶ 正在处理：{item.Title}");
                    var res = applyOptimization
                        ? await item.Module.ApplyAsync(AppendLog)
                        : await item.Module.RevertAsync(AppendLog);

                    AppendLog(res.Success ? $"  ✔ {res.Message}" : $"  ✖ {res.Message}");
                    if (res.Success) okCount++;
                    if (res.RecommendExplorerRestart) needExplorerRestart = true;
                    await item.RefreshStatusAsync();
                }
                finally
                {
                    item.IsBusy = false;
                }
            }

            AppendLog($"========== 批量任务完成：成功 {okCount} / {selected.Count} 项 ==========");
            ApplyFilters();
            if (needExplorerRestart)
            {
                ShowExplorerRestartBanner = true;
                var ask = MessageBox.Show(
                    "部分设置（如 Win10 经典右键菜单）需要重启 Windows 资源管理器才能立即生效。\n\n是否现在立即重启资源管理器？",
                    "需要重启资源管理器",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (ask == MessageBoxResult.Yes)
                {
                    await SystemHelper.RestartExplorerAsync(AppendLog);
                    ShowExplorerRestartBanner = false;
                }
            }
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task InstallSingleEnvAsync(EnvItemViewModel env)
    {
        IsGlobalBusy = true;
        env.IsBusy = true;
        try
        {
            var res = await EnvRegistry.InstallOrUpgradeAsync(env.Package, isUpgrade: env.IsInstalled, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            await env.RefreshStatusAsync();
            ApplyFilters();
        }
        finally
        {
            env.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private async Task UninstallSingleEnvAsync(EnvItemViewModel env)
    {
        var confirm = MessageBox.Show(
            $"确定要卸载「{env.Name}」吗？",
            "确认卸载",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsGlobalBusy = true;
        env.IsBusy = true;
        try
        {
            var res = await EnvRegistry.UninstallAsync(env.Package, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            await env.RefreshStatusAsync();
            ApplyFilters();
        }
        finally
        {
            env.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private async Task BatchInstallSelectedEnvsAsync()
    {
        var selected = FilteredEnvs.Where(x => x.IsSelectedForBatch).ToList();
        if (selected.Count == 0)
        {
            AppendLog("提示：请先勾选需要安装的开发环境（如 Python、Node.js、Git 等）。");
            MessageBox.Show("请先勾选需要批量安装的开发环境或工具！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsGlobalBusy = true;
        int okCount = 0;
        try
        {
            AppendLog($"========== 开始批量安装所选环境（共 {selected.Count} 项） ==========");
            foreach (var env in selected)
            {
                env.IsBusy = true;
                try
                {
                    var res = await EnvRegistry.InstallOrUpgradeAsync(env.Package, isUpgrade: env.IsInstalled, AppendLog);
                    AppendLog(res.Success ? $"  ✔ {res.Message}" : $"  ✖ {res.Message}");
                    if (res.Success)
                    {
                        okCount++;
                        env.IsSelectedForBatch = false;
                    }
                    await env.RefreshStatusAsync();
                }
                finally
                {
                    env.IsBusy = false;
                }
            }
            AppendLog($"========== 环境批量部署完成：成功 {okCount} / {selected.Count} 项 ==========");
            ApplyFilters();
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    public void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() => AppendLogCore(line));
        }
        else
        {
            AppendLogCore(line);
        }
    }

    private void AppendLogCore(string line)
    {
        _logBuilder.AppendLine(line);
        LogText = _logBuilder.ToString();
    }
}
