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
