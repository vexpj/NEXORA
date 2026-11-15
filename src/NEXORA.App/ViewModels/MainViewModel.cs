using NEXORA.App.Mvvm;
using NEXORA.Core.Appearance;

namespace NEXORA.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly HomeViewModel _home;
    private readonly VersionsViewModel _versions;
    private readonly ServersViewModel _servers;
    private PageViewModel _selectedPage;
    private bool _sidebarRight, _compact;
    private string? _bgImage;
    private double _bgOpacity;

    public MainViewModel(AppServices s)
    {
        _home = new HomeViewModel(s);
        _versions = new VersionsViewModel(s);
        _servers = new ServersViewModel(s);
        Pages = new PageViewModel[]
        {
            _home, _versions, _servers, new AccountViewModel(s),
            new ModsViewModel(s), new NewsViewModel(s), new SettingsViewModel(s),
        };
        _selectedPage = Pages[0];

        _servers.PlayRequested += ep => { SelectedPage = _home; _home.StartWithServer(ep); };
        s.AppearanceApplied += a => Ui.Run(() => ApplyAppearance(a));
        ApplyAppearance(s.Settings.Appearance);

        // Тихо восстанавливаем вход по сохранённому токену.
        _ = Task.Run(() => s.Account.RestoreAsync(CancellationToken.None));
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set { if (SetProperty(ref _selectedPage, value) && value == _servers) _servers.OnActivated(); }
    }

    public bool SidebarRight { get => _sidebarRight; private set => SetProperty(ref _sidebarRight, value); }
    public bool ShowNavLabels => !_compact;
    public double SidebarWidth => _compact ? 76 : 230;
    public string LogoText => _compact ? "N" : "NEXORA";
    public string? BackgroundImage { get => _bgImage; private set => SetProperty(ref _bgImage, value); }
    public double BackgroundOpacity { get => _bgOpacity; private set => SetProperty(ref _bgOpacity, value); }

    private void ApplyAppearance(AppearanceSettings a)
    {
        SidebarRight = a.SidebarRight;
        _compact = a.CompactSidebar;
        BackgroundOpacity = a.BackgroundOpacity;
        BackgroundImage = a.BackgroundImagePath is { } p && File.Exists(p) ? p : null;
        OnPropertyChanged(nameof(ShowNavLabels)); OnPropertyChanged(nameof(SidebarWidth)); OnPropertyChanged(nameof(LogoText));
    }

    /// <summary>Открыть файл сборки (двойной щелчок по .nexora-pack в проводнике).</summary>
    public void OpenPack(string path)
    {
        SelectedPage = _versions;
        _versions.LoadPackFile(path);
    }
}
