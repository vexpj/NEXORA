using NEXORA.App.ViewModels;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Appearance;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Auth;
using NEXORA.Infrastructure.Diagnostics;
using NEXORA.Infrastructure.Loaders;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Mods;
using NEXORA.Infrastructure.Net;
using NEXORA.Infrastructure.News;
using NEXORA.Infrastructure.Packs;
using NEXORA.Infrastructure.Perf;
using NEXORA.Infrastructure.Security;
using NEXORA.Infrastructure.Servers;
using NEXORA.Infrastructure.Storage;

namespace NEXORA.App;

/// <summary>Корень композиции: здесь создаются и связываются все службы.</summary>
public sealed class AppServices
{
    public const string LauncherVersion = "0.3.0";

    public required ISettingsStore SettingsStore { get; init; }
    public required LauncherSettings Settings { get; init; }
    public required LauncherPaths Paths { get; init; }
    public required SafeHttp Http { get; init; }
    public required VersionService Versions { get; init; }
    public required GameInstaller Installer { get; init; }
    public required JavaService Java { get; init; }
    public required LoaderService Loaders { get; init; }
    public required LaunchService Launcher { get; init; }
    public required AccountService Account { get; init; }
    public required ModrinthCatalog Modrinth { get; init; }
    public required CurseForgeCatalog CurseForge { get; init; }
    public required ModInstaller Mods { get; init; }
    public required NewsService News { get; init; }
    public required ProfilesModel Profiles { get; init; }
    public required CrashDiagnosticService Crash { get; init; }
    public required FixService Fixes { get; init; }
    public required PackService Packs { get; init; }
    public required IServerStore ServerStore { get; init; }
    public required ServerPinger Pinger { get; init; }

    /// <summary>Применяет тему к окну (задаётся из App.xaml.cs, чтобы слои логики не зависели от WPF).</summary>
    public Action<AppearanceSettings>? ThemeApplier { get; set; }
    public event Action<AppearanceSettings>? AppearanceApplied;

    public void ApplyAppearance(AppearanceSettings a)
    {
        a.Normalize();
        ThemeApplier?.Invoke(a);
        AppearanceApplied?.Invoke(a);
    }

    public PerformanceMonitor CreateMonitor(int pid) => new(pid);

    public static AppServices Create()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXORA");
        var protector = new DpapiProtector();
        var settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultFilePath(), protector);
        var settings = settingsStore.Load();

        var paths = new LauncherPaths(settings.GameDirectory, dataDir);
        var http = new SafeHttp();
        var dl = new FileDownloader(http);
        var versions = new VersionService(paths, http, dl);
        var installer = new GameInstaller(paths, versions, http, dl);
        var java = new JavaService(paths, http, dl);
        var loaders = new LoaderService(paths, http, dl, installer, java);
        var prov = new ModProvenanceStore(paths);
        var mods = new ModInstaller(paths, dl, prov);

        return new AppServices
        {
            SettingsStore = settingsStore, Settings = settings, Paths = paths, Http = http,
            Versions = versions, Installer = installer, Java = java, Loaders = loaders,
            Launcher = new LaunchService(paths, installer, java, loaders),
            Account = new AccountService(new MicrosoftAuthService(http), new JsonAccountStore(Path.Combine(dataDir, "account.json"), protector), http, () => settings.MicrosoftClientId),
            Modrinth = new ModrinthCatalog(http),
            CurseForge = new CurseForgeCatalog(http, () => settings.CurseForgeApiKey),
            Mods = mods,
            News = new NewsService(http),
            Profiles = new ProfilesModel(new JsonProfileStore(Path.Combine(dataDir, "profiles.json"))),
            Crash = new CrashDiagnosticService(paths),
            Fixes = new FixService(paths, settingsStore, settings, mods),
            Packs = new PackService(paths, prov, dl),
            ServerStore = new JsonServerStore(Path.Combine(dataDir, "servers.json")),
            Pinger = new ServerPinger(),
        };
    }
}
