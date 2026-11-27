using WinOptimizer.Core;

namespace WinOptimizer.ViewModels;

public sealed class TweakItemViewModel : ObservableObject
{
    public ITweakModule Module { get; }

    private bool _isSelectedForBatch;
    private bool _isOptimized;
    private bool _isBusy;
    private string _statusBadgeText = "正在检测...";

    public TweakItemViewModel(ITweakModule module)
    {
        Module = module;
    }

    public string Title => Module.Title;
    public string Description => Module.Description;
    public string TechnicalDetail => Module.TechnicalDetail;
    public string Category => Module.Category;
    public string IconGlyph => Module.IconGlyph;
    public bool NeedsExplorerRestart => Module.NeedsExplorerRestart;
    public TweakRestartRequirement RestartRequirement => Module.RestartRequirement;
    public bool IsSystemRebootRequired => RestartRequirement == TweakRestartRequirement.SystemReboot;
    public bool IsExplorerRestartRequired => RestartRequirement == TweakRestartRequirement.ExplorerRestart;
    public bool IsInstantEffect => RestartRequirement == TweakRestartRequirement.None;

    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set => SetProperty(ref _isSelectedForBatch, value);
    }

    public bool IsOptimized
    {
        get => _isOptimized;
        private set => SetProperty(ref _isOptimized, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusBadgeText
    {
        get => _statusBadgeText;
        private set => SetProperty(ref _statusBadgeText, value);
    }

    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            var status = await Module.CheckStatusAsync();
            IsOptimized = status.IsOptimized;
            StatusBadgeText = status.StatusBadgeText;
        }
        catch (Exception ex)
        {
            StatusBadgeText = $"检测异常: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class EnvItemViewModel : ObservableObject
{
    public EnvPackageDefinition Package { get; }

    private bool _isSelectedForBatch;
    private bool _isInstalled;
    private bool _isBusy;
    private string _versionBadgeText = "正在检测...";

    public EnvItemViewModel(EnvPackageDefinition package)
    {
        Package = package;
    }

    public string Name => Package.Name;
    public string Category => Package.Category;
    public string Description => Package.Description;
    public string WingetId => $"winget: {Package.WingetId}";
    public string IconGlyph => Package.IconGlyph;
    public bool IsRecommendedPreset => Package.IsRecommendedPreset;

    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set => SetProperty(ref _isSelectedForBatch, value);
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
                OnPropertyChanged(nameof(PrimaryActionText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string VersionBadgeText
    {
        get => _versionBadgeText;
        private set => SetProperty(ref _versionBadgeText, value);
    }

    public string PrimaryActionText => IsInstalled ? "检查更新" : "立即安装";

    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            var status = await Package.CheckInstalledAsync();
            IsInstalled = status.IsInstalled;
            VersionBadgeText = status.VersionText;
        }
        catch
        {
            VersionBadgeText = "检测失败";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class ContextMenuItemViewModel : ObservableObject
{
    public ContextMenuEntry Entry { get; }

    private bool _isSelectedForBatch;
    private bool _isMenuEnabled;
    private bool _isBusy;

    public ContextMenuItemViewModel(ContextMenuEntry entry)
    {
        Entry = entry;
        _isMenuEnabled = entry.IsEnabled;
    }

    public string DisplayName => Entry.DisplayName;
    public string KeyName => Entry.KeyName;
    public string ScopeKey => Entry.ScopeKey;
    public string ScopeLabel => Entry.ScopeLabel;
    public string KindLabel => Entry.KindLabel;
    public string FullRegistryPath => Entry.FullRegistryPath;
    public bool CanDelete => !Entry.IsProtectedSystemItem;

    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set => SetProperty(ref _isSelectedForBatch, value);
    }

    public bool IsMenuEnabled
    {
        get => _isMenuEnabled;
        set
        {
            if (SetProperty(ref _isMenuEnabled, value))
            {
                Entry.IsEnabled = value;
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusBadgeText => IsMenuEnabled ? "已显示" : "已隐藏";
}

public sealed class StartupItemViewModel : ObservableObject
{
    public StartupEntry Entry { get; }

    private bool _isSelectedForBatch;
    private bool _isStartupEnabled;
    private bool _isBusy;

    public StartupItemViewModel(StartupEntry entry)
    {
        Entry = entry;
        _isStartupEnabled = entry.IsEnabled;
    }

    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string SourceLabel => Entry.SourceLabel;
    public bool TargetFileMissing => !Entry.TargetFileExists;
    public bool CanLocate => Entry.TargetFileExists || !string.IsNullOrWhiteSpace(Entry.ExecutablePath);

    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set => SetProperty(ref _isSelectedForBatch, value);
    }

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set
        {
            if (SetProperty(ref _isStartupEnabled, value))
            {
                Entry.IsEnabled = value;
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusBadgeText => IsStartupEnabled ? "开机自启" : "已禁用";
}
