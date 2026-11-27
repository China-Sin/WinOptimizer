using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using WinOptimizer.Core;
using WinOptimizer.Tweaks;

namespace WinOptimizer.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly List<TweakItemViewModel> _allTweaks;
    private readonly List<EnvItemViewModel> _allEnvs;
    private readonly List<ContextMenuItemViewModel> _allContextMenus = [];
    private readonly List<StartupItemViewModel> _allStartupItems = [];
    private readonly HashSet<string> _pendingRebootTitles = [];
    private readonly StringBuilder _logBuilder = new();

    private string _selectedNavKey = "ALL_TWEAKS";
    private string _statusFilter = "ALL"; // ALL | OPTIMIZED | UNOPTIMIZED
    private string _ctxScopeFilter = "DESKTOP"; // ALL | DESKTOP | FILE | FOLDER | DRIVE
    private string _startupFilter = "ALL"; // ALL | ENABLED | DISABLED
    private string _searchQuery = string.Empty;
    private bool _isGlobalBusy;
    private bool _showExplorerRestartBanner;
    private bool _showSystemRebootBanner;
    private string _systemRebootBannerText = "提示：部分底层优化项在开启或关闭（恢复默认）后，需重启电脑才能完全生效。";
    private string _logText = string.Empty;
    private string _pageHeaderTitle = "全部系统与游戏优化";
    private string _pageHeaderSubtitle = "实时检测系统当前状态，支持单项开关切换与一键批量优化 / 恢复默认";
    private string _fileLockTargetPath = string.Empty;
    private string _fileLockSummaryText = "将无法删除的文件或文件夹拖入窗口，或点击上方「选择文件 / 选择文件夹」扫描占用进程。";
    private bool _isRightClickUnlockIntegrated;

    public ObservableCollection<TweakItemViewModel> FilteredTweaks { get; } = [];
    public ObservableCollection<EnvItemViewModel> FilteredEnvs { get; } = [];
    public ObservableCollection<ContextMenuItemViewModel> FilteredContextMenus { get; } = [];
    public ObservableCollection<StartupItemViewModel> FilteredStartupItems { get; } = [];
    public ObservableCollection<FileLockItemViewModel> FileLocks { get; } = [];

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
    public RelayCommand DismissExplorerBannerCommand { get; }
    public RelayCommand RebootComputerCommand { get; }
    public RelayCommand DismissRebootBannerCommand { get; }
    public RelayCommand PurgeStandbyRamCommand { get; }
    public RelayCommand CleanShaderCacheCommand { get; }

    public RelayCommand InstallOrUpgradeSingleEnvCommand { get; }
    public RelayCommand UninstallSingleEnvCommand { get; }
    public RelayCommand SelectRecommendedEnvsCommand { get; }
    public RelayCommand InstallSelectedEnvsCommand { get; }
    public RelayCommand SetCtxScopeFilterCommand { get; }
    public RelayCommand ToggleSingleContextMenuCommand { get; }
    public RelayCommand DeleteSingleContextMenuCommand { get; }
    public RelayCommand HideSelectedContextMenusCommand { get; }
    public RelayCommand EnableSelectedContextMenusCommand { get; }
    public RelayCommand SetStartupFilterCommand { get; }
    public RelayCommand ToggleSingleStartupCommand { get; }
    public RelayCommand LocateStartupFileCommand { get; }
    public RelayCommand DeleteSingleStartupCommand { get; }
    public RelayCommand DisableSelectedStartupsCommand { get; }
    public RelayCommand EnableSelectedStartupsCommand { get; }
    public RelayCommand BrowseLockFileCommand { get; }
    public RelayCommand BrowseLockFolderCommand { get; }
    public RelayCommand ScanFileLocksCommand { get; }
    public RelayCommand UnlockAllLocksCommand { get; }
    public RelayCommand UnlockAndDeleteTargetCommand { get; }
    public RelayCommand ScheduleDeleteOnRebootCommand { get; }
    public RelayCommand CloseSingleHandleCommand { get; }
    public RelayCommand KillSingleLockProcessCommand { get; }
    public RelayCommand LocateLockProcessCommand { get; }
    public RelayCommand ToggleUnlockRightClickMenuCommand { get; }
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

        DismissExplorerBannerCommand = new RelayCommand(_ => ShowExplorerRestartBanner = false);

        RebootComputerCommand = new RelayCommand(
            _ =>
            {
                var confirm = MessageBox.Show(
                    "确定要现在立即重启电脑吗？\n\n请先确保 Premiere Pro、浏览器或文档中的未保存工作已经保存完毕。",
                    "确认重启电脑",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                AppendLog("正在执行系统重启命令 (shutdown /r /t 0)...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            },
            _ => !IsGlobalBusy);

        DismissRebootBannerCommand = new RelayCommand(_ => ShowSystemRebootBanner = false);

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

        SetCtxScopeFilterCommand = new RelayCommand(p =>
        {
            if (p is string scope)
                CtxScopeFilter = scope;
        });

        ToggleSingleContextMenuCommand = new RelayCommand(
            async p => { if (p is ContextMenuItemViewModel item) await ToggleSingleContextMenuAsync(item); },
            _ => !IsGlobalBusy);

        DeleteSingleContextMenuCommand = new RelayCommand(
            async p => { if (p is ContextMenuItemViewModel item) await DeleteSingleContextMenuAsync(item); },
            _ => !IsGlobalBusy);

        HideSelectedContextMenusCommand = new RelayCommand(
            async _ => await BatchSetContextMenusAsync(enable: false),
            _ => !IsGlobalBusy);

        EnableSelectedContextMenusCommand = new RelayCommand(
            async _ => await BatchSetContextMenusAsync(enable: true),
            _ => !IsGlobalBusy);

        SetStartupFilterCommand = new RelayCommand(p =>
        {
            if (p is string filter)
                StartupFilter = filter;
        });

        ToggleSingleStartupCommand = new RelayCommand(
            async p => { if (p is StartupItemViewModel item) await ToggleSingleStartupAsync(item); },
            _ => !IsGlobalBusy);

        LocateStartupFileCommand = new RelayCommand(p =>
        {
            if (p is StartupItemViewModel item)
            {
                StartupItemManager.OpenFileLocation(item.Entry);
                AppendLog($"已在资源管理器定位启动项文件：{item.Entry.ExecutablePath}");
            }
        });

        DeleteSingleStartupCommand = new RelayCommand(
            async p => { if (p is StartupItemViewModel item) await DeleteSingleStartupAsync(item); },
            _ => !IsGlobalBusy);

        DisableSelectedStartupsCommand = new RelayCommand(
            async _ => await BatchSetStartupsAsync(enable: false),
            _ => !IsGlobalBusy);

        EnableSelectedStartupsCommand = new RelayCommand(
            async _ => await BatchSetStartupsAsync(enable: true),
            _ => !IsGlobalBusy);

        BrowseLockFileCommand = new RelayCommand(_ =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择需要解除占用的文件",
                Filter = "所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                FileLockTargetPath = dlg.FileName;
                _ = ScanFileLocksAsync();
            }
        });

        BrowseLockFolderCommand = new RelayCommand(_ =>
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择需要解除占用的文件夹"
            };
            if (dlg.ShowDialog() == true)
            {
                FileLockTargetPath = dlg.FolderName;
                _ = ScanFileLocksAsync();
            }
        });

        ScanFileLocksCommand = new RelayCommand(
            async _ => await ScanFileLocksAsync(),
            _ => !IsGlobalBusy);

        UnlockAllLocksCommand = new RelayCommand(
            async _ => await UnlockAllLocksAsync(),
            _ => !IsGlobalBusy);

        UnlockAndDeleteTargetCommand = new RelayCommand(
            async _ => await UnlockAndForceDeleteAsync(),
            _ => !IsGlobalBusy);

        ScheduleDeleteOnRebootCommand = new RelayCommand(
            async _ => await ScheduleDeleteOnRebootAsync(),
            _ => !IsGlobalBusy);

        CloseSingleHandleCommand = new RelayCommand(
            async p => { if (p is FileLockItemViewModel item) await CloseSingleLockHandleAsync(item); },
            _ => !IsGlobalBusy);

        KillSingleLockProcessCommand = new RelayCommand(
            async p => { if (p is FileLockItemViewModel item) await KillSingleLockProcessAsync(item); },
            _ => !IsGlobalBusy);

        LocateLockProcessCommand = new RelayCommand(p =>
        {
            if (p is FileLockItemViewModel item && File.Exists(item.Info.ProcessPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.Info.ProcessPath}\"",
                    UseShellExecute = true
                });
                AppendLog($"已在资源管理器定位占用进程程序：{item.Info.ProcessPath}");
            }
        });

        ToggleUnlockRightClickMenuCommand = new RelayCommand(_ =>
        {
            bool targetEnable = !IsRightClickUnlockIntegrated;
            var res = FileLockManager.SetRightClickIntegration(targetEnable, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            IsRightClickUnlockIntegrated = FileLockManager.IsRightClickIntegrationEnabled();
        });

        ClearLogCommand = new RelayCommand(_ =>
        {
            _logBuilder.Clear();
            LogText = string.Empty;
        });

        IsRightClickUnlockIntegrated = FileLockManager.IsRightClickIntegrationEnabled();
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
                OnPropertyChanged(nameof(IsContextMenuView));
                OnPropertyChanged(nameof(IsStartupView));
                OnPropertyChanged(nameof(IsFileLockView));
                UpdateHeaders();
                ApplyFilters();
            }
        }
    }

    public bool IsTweaksView => !IsEnvView && !IsContextMenuView && !IsStartupView && !IsFileLockView;
    public bool IsEnvView => SelectedNavKey.StartsWith("ENV_", StringComparison.Ordinal);
    public bool IsContextMenuView => SelectedNavKey == "CTX_MENU";
    public bool IsStartupView => SelectedNavKey == "STARTUP_MGR";
    public bool IsFileLockView => SelectedNavKey == "FILE_LOCK";

    public string FileLockTargetPath
    {
        get => _fileLockTargetPath;
        set => SetProperty(ref _fileLockTargetPath, value);
    }

    public string FileLockSummaryText
    {
        get => _fileLockSummaryText;
        private set => SetProperty(ref _fileLockSummaryText, value);
    }

    public bool IsRightClickUnlockIntegrated
    {
        get => _isRightClickUnlockIntegrated;
        private set
        {
            if (SetProperty(ref _isRightClickUnlockIntegrated, value))
                OnPropertyChanged(nameof(RightClickUnlockButtonText));
        }
    }

    public string RightClickUnlockButtonText => IsRightClickUnlockIntegrated ? "移除右键「解除占用」" : "添加到系统右键菜单";
    public bool HasFileLocks => FileLocks.Count > 0;

    public string StartupFilter
    {
        get => _startupFilter;
        set
        {
            if (SetProperty(ref _startupFilter, value))
                ApplyFilters();
        }
    }

    public string FilterAllStartupsLabel => $"全部 ({_allStartupItems.Count})";
    public string FilterEnabledStartupsLabel => $"开机自启 ({_allStartupItems.Count(x => x.IsStartupEnabled)})";
    public string FilterDisabledStartupsLabel => $"已禁用 ({_allStartupItems.Count(x => !x.IsStartupEnabled)})";

    public string CtxScopeFilter
    {
        get => _ctxScopeFilter;
        set
        {
            if (SetProperty(ref _ctxScopeFilter, value))
                ApplyFilters();
        }
    }

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

    public bool ShowSystemRebootBanner
    {
        get => _showSystemRebootBanner;
        set => SetProperty(ref _showSystemRebootBanner, value);
    }

    public string SystemRebootBannerText
    {
        get => _systemRebootBannerText;
        private set => SetProperty(ref _systemRebootBannerText, value);
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
        AppendLog("正在并行扫描系统优化项、开发环境、右键菜单与开机启动项...");
        var tweakTasks = _allTweaks.Select(t => t.RefreshStatusAsync());
        var envTasks = _allEnvs.Select(e => e.RefreshStatusAsync());
        var ctxTask = ReloadContextMenusInternalAsync();
        var startupTask = ReloadStartupItemsInternalAsync();
        await Task.WhenAll(tweakTasks.Concat(envTasks).Append(ctxTask).Append(startupTask));
        ApplyFilters();
        AppendLog($"全部状态扫描完毕（右键可见项 {_allContextMenus.Count} 个，开机启动项 {_allStartupItems.Count} 个）。");
    }

    private async Task ReloadContextMenusInternalAsync()
    {
        var entries = await ContextMenuManager.ScanAllAsync();
        _allContextMenus.Clear();
        _allContextMenus.AddRange(entries.Select(e => new ContextMenuItemViewModel(e)));
    }

    private async Task ReloadStartupItemsInternalAsync()
    {
        var entries = await StartupItemManager.ScanAllAsync();
        _allStartupItems.Clear();
        _allStartupItems.AddRange(entries.Select(e => new StartupItemViewModel(e)));
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
            else if (IsEnvView)
            {
                AppendLog("正在重新检测本地开发环境版本状态...");
                await Task.WhenAll(_allEnvs.Select(e => e.RefreshStatusAsync()));
            }
            else if (IsContextMenuView)
            {
                AppendLog("正在重新扫描桌面、文件与文件夹右键菜单注册表项...");
                await ReloadContextMenusInternalAsync();
            }
            else if (IsStartupView)
            {
                AppendLog("正在重新扫描注册表 Run 与启动文件夹开机项...");
                await ReloadStartupItemsInternalAsync();
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
            "CTX_MENU" => ("右键菜单自定义管理", "所见即所得无损隐藏或恢复桌面、文件、文件夹右键项目（绝大部分下一次右键即刻生效）"),
            "STARTUP_MGR" => ("开机启动项管理", "与系统任务管理器 100% 同步：无损启用/禁用自启项、一键定位源文件与清理残留失效项"),
            "FILE_LOCK" => ("文件 / 文件夹占用解除", "三重引擎精准定位锁定进程与 DLL 模块：支持不杀进程强断句柄、结束锁定进程、强制删除与重启自动清除"),
            "ENV_ALL" => ("开发环境与工具安装", "基于微软官方 winget 静默部署本机必备 Python / Node.js / Git / .NET / VC++ 环境"),
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
        OnPropertyChanged(nameof(FilterAllStartupsLabel));
        OnPropertyChanged(nameof(FilterEnabledStartupsLabel));
        OnPropertyChanged(nameof(FilterDisabledStartupsLabel));
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

        FilteredContextMenus.Clear();
        if (IsContextMenuView)
        {
            foreach (var cm in _allContextMenus)
            {
                if (CtxScopeFilter != "ALL" && cm.ScopeKey != CtxScopeFilter)
                    continue;
                if (!string.IsNullOrEmpty(q) &&
                    !cm.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !cm.KeyName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !cm.FullRegistryPath.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                FilteredContextMenus.Add(cm);
            }
        }

        FilteredStartupItems.Clear();
        if (IsStartupView)
        {
            foreach (var st in _allStartupItems)
            {
                if (StartupFilter == "ENABLED" && !st.IsStartupEnabled)
                    continue;
                if (StartupFilter == "DISABLED" && st.IsStartupEnabled)
                    continue;
                if (!string.IsNullOrEmpty(q) &&
                    !st.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !st.Command.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !st.SourceLabel.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                FilteredStartupItems.Add(st);
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
            if (result.Success)
            {
                if (result.RecommendExplorerRestart || item.IsExplorerRestartRequired)
                {
                    ShowExplorerRestartBanner = true;
                    AppendLog($"  ℹ 「{item.Title}」开启或关闭后均需重启资源管理器才能完全生效。");
                }
                if (result.RecommendSystemReboot || item.IsSystemRebootRequired)
                {
                    MarkPendingSystemReboot(item.Title);
                    AppendLog($"  ⚠ 「{item.Title}」属于底层内核/驱动配置，开启或关闭后均需重启电脑才能完全生效！");
                }
            }

            await item.RefreshStatusAsync();
            ApplyFilters();
        }
        finally
        {
            item.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private void MarkPendingSystemReboot(string itemTitle)
    {
        _pendingRebootTitles.Add(itemTitle);
        string summary = _pendingRebootTitles.Count == 1
            ? $"「{_pendingRebootTitles.First()}」"
            : $"「{_pendingRebootTitles.First()}」等 {_pendingRebootTitles.Count} 项底层设置";
        SystemRebootBannerText = $"提示：你刚才变更了{summary}（开启或恢复默认均需重启电脑后才能完全生效）。";
        ShowSystemRebootBanner = true;
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
        var rebootTitles = new List<string>();
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
                    if (res.Success)
                    {
                        okCount++;
                        if (res.RecommendExplorerRestart || item.IsExplorerRestartRequired)
                            needExplorerRestart = true;
                        if (res.RecommendSystemReboot || item.IsSystemRebootRequired)
                        {
                            rebootTitles.Add(item.Title);
                            MarkPendingSystemReboot(item.Title);
                        }
                    }
                    await item.RefreshStatusAsync();
                }
                finally
                {
                    item.IsBusy = false;
                }
            }

            AppendLog($"========== 批量任务完成：成功 {okCount} / {selected.Count} 项 ==========");
            ApplyFilters();

            if (rebootTitles.Count > 0)
            {
                AppendLog($"⚠ 以下 {rebootTitles.Count} 项需重启电脑生效：{string.Join("、", rebootTitles)}");
            }

            if (needExplorerRestart && rebootTitles.Count == 0)
            {
                ShowExplorerRestartBanner = true;
                var ask = MessageBox.Show(
                    "部分设置（如右键菜单、开始菜单或任务栏习惯）在开启或关闭后需要重启 Windows 资源管理器才能立即生效。\n\n是否现在立即重启资源管理器？",
                    "需要重启资源管理器",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (ask == MessageBoxResult.Yes)
                {
                    await SystemHelper.RestartExplorerAsync(AppendLog);
                    ShowExplorerRestartBanner = false;
                }
            }
            else if (rebootTitles.Count > 0)
            {
                if (needExplorerRestart) ShowExplorerRestartBanner = true;
                var askReboot = MessageBox.Show(
                    $"以下 {rebootTitles.Count} 项底层设置（无论开启或恢复默认）均需重启电脑才能完全生效：\n\n• {string.Join("\n• ", rebootTitles)}\n\n是否现在立即重启电脑？（请先保存手头未保存的工作）",
                    "需要重启电脑生效",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (askReboot == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 0",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
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

    private async Task ToggleSingleContextMenuAsync(ContextMenuItemViewModel item)
    {
        IsGlobalBusy = true;
        item.IsBusy = true;
        try
        {
            bool targetEnable = !item.IsMenuEnabled;
            var res = await ContextMenuManager.SetEnabledAsync(item.Entry, targetEnable, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            if (res.Success)
                item.IsMenuEnabled = targetEnable;
            if (res.RecommendExplorerRestart)
                ShowExplorerRestartBanner = true;
        }
        finally
        {
            item.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private async Task DeleteSingleContextMenuAsync(ContextMenuItemViewModel item)
    {
        var confirm = MessageBox.Show(
            $"确定要永久删除右键注册表项「{item.DisplayName}」吗？\n\n路径：{item.FullRegistryPath}\n\n（如果只是想先藏起来以后方便恢复，建议点「否」并直接关闭右侧开关即可）",
            "确认永久删除右键项",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsGlobalBusy = true;
        try
        {
            var res = await ContextMenuManager.DeleteEntryAsync(item.Entry, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            if (res.Success)
            {
                _allContextMenus.Remove(item);
                ApplyFilters();
            }
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task BatchSetContextMenusAsync(bool enable)
    {
        var selected = FilteredContextMenus.Where(x => x.IsSelectedForBatch).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选需要批量操作的右键菜单项！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsGlobalBusy = true;
        int ok = 0;
        bool needRestart = false;
        try
        {
            AppendLog($"========== 开始批量{(enable ? "恢复显示" : "隐藏")}右键项（共 {selected.Count} 项） ==========");
            foreach (var item in selected)
            {
                var res = await ContextMenuManager.SetEnabledAsync(item.Entry, enable, AppendLog);
                AppendLog(res.Success ? $"  ✔ {res.Message}" : $"  ✖ {res.Message}");
                if (res.Success)
                {
                    item.IsMenuEnabled = enable;
                    item.IsSelectedForBatch = false;
                    ok++;
                }
                if (res.RecommendExplorerRestart) needRestart = true;
            }
            AppendLog($"========== 批量右键管理完成：成功 {ok} / {selected.Count} 项 ==========");
            if (needRestart) ShowExplorerRestartBanner = true;
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task ToggleSingleStartupAsync(StartupItemViewModel item)
    {
        IsGlobalBusy = true;
        item.IsBusy = true;
        try
        {
            bool targetEnable = !item.IsStartupEnabled;
            var res = await StartupItemManager.SetEnabledAsync(item.Entry, targetEnable, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            if (res.Success)
            {
                item.IsStartupEnabled = targetEnable;
                ApplyFilters();
            }
        }
        finally
        {
            item.IsBusy = false;
            IsGlobalBusy = false;
        }
    }

    private async Task DeleteSingleStartupAsync(StartupItemViewModel item)
    {
        var confirm = MessageBox.Show(
            $"确定要永久删除开机启动项「{item.Name}」吗？\n\n命令：{item.Command}\n\n（如果只是暂时不想让它开机启动，点「否」直接关闭右侧开关即可无损禁用）",
            "确认删除开机启动项",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsGlobalBusy = true;
        try
        {
            var res = await StartupItemManager.DeleteEntryAsync(item.Entry, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            if (res.Success)
            {
                _allStartupItems.Remove(item);
                ApplyFilters();
            }
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task BatchSetStartupsAsync(bool enable)
    {
        var selected = FilteredStartupItems.Where(x => x.IsSelectedForBatch).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选需要批量操作的开机启动项！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsGlobalBusy = true;
        int ok = 0;
        try
        {
            AppendLog($"========== 开始批量{(enable ? "启用" : "禁用")}开机启动项（共 {selected.Count} 项） ==========");
            foreach (var item in selected)
            {
                var res = await StartupItemManager.SetEnabledAsync(item.Entry, enable, AppendLog);
                AppendLog(res.Success ? $"  ✔ {res.Message}" : $"  ✖ {res.Message}");
                if (res.Success)
                {
                    item.IsStartupEnabled = enable;
                    item.IsSelectedForBatch = false;
                    ok++;
                }
            }
            AppendLog($"========== 批量启动项管理完成：成功 {ok} / {selected.Count} 项 ==========");
            ApplyFilters();
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    public void OpenFileUnlockerWithPath(string path)
    {
        SelectedNavKey = "FILE_LOCK";
        FileLockTargetPath = FileLockManager.NormalizeTargetPath(path);
        _ = ScanFileLocksAsync();
    }

    public async Task ScanFileLocksAsync()
    {
        string normalized = FileLockManager.NormalizeTargetPath(FileLockTargetPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            FileLockSummaryText = "请先粘贴路径、点击「选择文件 / 选择文件夹」，或直接把文件拖入此窗口。";
            return;
        }

        FileLockTargetPath = normalized;
        IsGlobalBusy = true;
        FileLocks.Clear();
        OnPropertyChanged(nameof(HasFileLocks));
        FileLockSummaryText = $"正在深度扫描占用：{normalized} ...";

        try
        {
            AppendLog($"▶ 开始扫描文件/文件夹占用：{normalized}");
            var locks = await FileLockManager.ScanLocksAsync(normalized, AppendLog);
            foreach (var info in locks)
                FileLocks.Add(new FileLockItemViewModel(info));

            OnPropertyChanged(nameof(HasFileLocks));

            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                FileLockSummaryText = "目标文件或文件夹已不存在。";
                AppendLog("ℹ 目标路径当前已不存在磁盘上。");
            }
            else if (locks.Count == 0)
            {
                FileLockSummaryText = "✔ 未检测到任何进程占用此路径！现在可以直接删除或重命名（若仍报错可点「解除占用并强制删除」清除只读属性）。";
                AppendLog("✔ 扫描完成：当前没有任何进程锁定该路径。");
            }
            else
            {
                FileLockSummaryText = $"⚠ 共检测到 {locks.Count} 个进程正在占用此路径，可点击单项断开句柄 / 结束进程，或点上方「一键解除全部占用」。";
                AppendLog($"⚠ 扫描完成：共发现 {locks.Count} 个占用进程。");
            }
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task CloseSingleLockHandleAsync(FileLockItemViewModel item)
    {
        IsGlobalBusy = true;
        try
        {
            var res = await FileLockManager.CloseHandlesOnlyAsync(item.Info, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
        }
        finally
        {
            IsGlobalBusy = false;
        }
        await ScanFileLocksAsync();
    }

    private async Task KillSingleLockProcessAsync(FileLockItemViewModel item)
    {
        if (!item.IsExplorer)
        {
            var confirm = MessageBox.Show(
                $"确定要强制结束进程「{item.Info.ProcessName} (PID: {item.ProcessId})」以解除文件占用吗？",
                "确认结束占用进程",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        IsGlobalBusy = true;
        try
        {
            var res = await FileLockManager.TerminateLockingProcessAsync(item.Info, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
        }
        finally
        {
            IsGlobalBusy = false;
        }
        await ScanFileLocksAsync();
    }

    private async Task UnlockAllLocksAsync()
    {
        string normalized = FileLockManager.NormalizeTargetPath(FileLockTargetPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            MessageBox.Show("请先选择或拖入需要解除占用的文件/文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (FileLocks.Count == 0)
            await ScanFileLocksAsync();

        if (FileLocks.Count == 0)
            return;

        IsGlobalBusy = true;
        try
        {
            var list = FileLocks.Select(x => x.Info).ToList();
            var res = await FileLockManager.UnlockAllAsync(list, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
        }
        finally
        {
            IsGlobalBusy = false;
        }
        await ScanFileLocksAsync();
    }

    private async Task UnlockAndForceDeleteAsync()
    {
        string normalized = FileLockManager.NormalizeTargetPath(FileLockTargetPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            MessageBox.Show("请先选择或拖入需要强制删除的文件/文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(normalized) && !Directory.Exists(normalized))
        {
            MessageBox.Show("目标文件或文件夹已经不存在了。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要解除占用并【永久删除】以下目标吗？（不经过回收站，删除后无法撤销！）\n\n{normalized}",
            "确认解除占用并强制删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsGlobalBusy = true;
        try
        {
            AppendLog($"========== 开始解除占用并强制删除：{normalized} ==========");
            var locks = await FileLockManager.ScanLocksAsync(normalized, AppendLog);
            if (locks.Count > 0)
            {
                await FileLockManager.UnlockAllAsync(locks, AppendLog);
            }

            var (deleted, msg) = await FileLockManager.ForceDeleteTargetAsync(normalized, AppendLog);
            AppendLog(deleted ? $"✔ {msg}" : $"✖ {msg}");

            if (deleted)
            {
                FileLocks.Clear();
                OnPropertyChanged(nameof(HasFileLocks));
                FileLockSummaryText = $"✔ 已成功解除占用并彻底删除：{normalized}";
            }
            else
            {
                var askRebootDel = MessageBox.Show(
                    $"{msg}\n\n是否登记为「下次重启电脑时由 Windows 内核自动强制删除」？",
                    "无法即时删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (askRebootDel == MessageBoxResult.Yes)
                {
                    var schedRes = await FileLockManager.ScheduleDeleteOnRebootAsync(normalized, AppendLog);
                    AppendLog(schedRes.Success ? $"✔ {schedRes.Message}" : $"✖ {schedRes.Message}");
                    if (schedRes.RecommendSystemReboot)
                        MarkPendingSystemReboot($"重启自动删除 {Path.GetFileName(normalized)}");
                }
            }
        }
        finally
        {
            IsGlobalBusy = false;
        }
    }

    private async Task ScheduleDeleteOnRebootAsync()
    {
        string normalized = FileLockManager.NormalizeTargetPath(FileLockTargetPath);
        if (string.IsNullOrWhiteSpace(normalized) || (!File.Exists(normalized) && !Directory.Exists(normalized)))
        {
            MessageBox.Show("请先选择或拖入存在的目标文件/文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要登记下次重启电脑时自动永久删除吗？\n\n{normalized}",
            "确认重启时自动删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsGlobalBusy = true;
        try
        {
            var res = await FileLockManager.ScheduleDeleteOnRebootAsync(normalized, AppendLog);
            AppendLog(res.Success ? $"✔ {res.Message}" : $"✖ {res.Message}");
            if (res.Success)
            {
                FileLockSummaryText = $"✔ {res.Message}";
                if (res.RecommendSystemReboot)
                    MarkPendingSystemReboot($"重启自动删除 {Path.GetFileName(normalized)}");
            }
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
