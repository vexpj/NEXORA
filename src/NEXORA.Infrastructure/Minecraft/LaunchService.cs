using System.Diagnostics;
using NEXORA.Core.Auth;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Loaders;

namespace NEXORA.Infrastructure.Minecraft;

public sealed class GameProcess
{
    private readonly Process _process;
    public GameProcess(Process p, string logPath) { _process = p; LogPath = logPath; }
    public string LogPath { get; }
    public bool HasExited => _process.HasExited;
    public void Kill() { try { _process.Kill(true); } catch (InvalidOperationException) { } }
}

public sealed class LaunchService
{
    private readonly LauncherPaths _paths;
    private readonly GameInstaller _installer;
    private readonly JavaService _java;
    private readonly LoaderService _loaders;

    public LaunchService(LauncherPaths paths, GameInstaller installer, JavaService java, LoaderService loaders)
    { _paths = paths; _installer = installer; _java = java; _loaders = loaders; }

    /// <summary>Устанавливает всё недостающее и запускает игру. onProfileChanged вызывается, если у профиля появился id версии загрузчика.</summary>
    public async Task<GameProcess> LaunchAsync(GameProfile profile, MinecraftSession session, LauncherSettings settings,
        IProgress<InstallProgress>? progress, Action<string> onLine, Action<int> onExit, Action<GameProfile> onProfileChanged, CancellationToken ct)
    {
        var gameDir = _paths.InstanceDir(profile.Id);
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));

        // 1. загрузчик (Fabric/Forge/NeoForge), если ещё не установлен
        if (profile.Loader != LoaderType.Vanilla &&
            (string.IsNullOrEmpty(profile.InstalledVersionId) || !File.Exists(_paths.VersionJson(profile.InstalledVersionId))))
        {
            progress?.Report(new("Подготовка Minecraft " + profile.MinecraftVersion, 0));
            await _installer.InstallAsync(profile.MinecraftVersion, gameDir, progress, ct);
            profile.InstalledVersionId = await _loaders.InstallAsync(profile, progress, ct);
            onProfileChanged(profile);
        }

        // 2. игра, библиотеки, ресурсы
        var installed = await _installer.InstallAsync(profile.LaunchVersionId, gameDir, progress, ct);

        // 3. Java
        var javaw = await _java.ResolveAsync(settings.JavaPath, installed.Info, progress, ct);

        // 4. аргументы
        var ctx = new LaunchContext
        {
            PlayerName = session.Profile.Name, Uuid = session.Profile.Id, AccessToken = session.AccessToken,
            VersionName = installed.LaunchId, VersionType = installed.Info.Type,
            GameDirectory = gameDir, AssetsRoot = _paths.Assets, AssetsIndexName = installed.AssetsIndexName,
            GameAssetsDir = installed.GameAssetsDir, LibraryDirectory = _paths.Libraries, NativesDirectory = installed.NativesDir,
            Classpath = installed.Classpath, RamMb = profile.RamMb ?? settings.RamMb, ExtraJvmArgs = profile.JvmArgs,
            LoggingConfigPath = installed.LoggingConfigPath, ClientId = settings.MicrosoftClientId,
        };
        var args = LaunchArgumentsBuilder.Build(installed.Info, ctx);

        var psi = new ProcessStartInfo(javaw)
        {
            WorkingDirectory = gameDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Directory.CreateDirectory(_paths.LogsDir);
        var logPath = Path.Combine(_paths.LogsDir, "latest.log");
        var writer = new StreamWriter(logPath, false) { AutoFlush = true };
        var token = session.AccessToken;
        void Line(string? l)
        {
            if (l is null) return;
            l = l.Replace(token, "***");
            lock (writer) writer.WriteLine(l);
            onLine(l);
        }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => Line(e.Data);
        proc.ErrorDataReceived += (_, e) => Line(e.Data);
        proc.Exited += (_, _) => { lock (writer) writer.Dispose(); onExit(proc.ExitCode); };
        Line($"[NEXORA] Запуск {profile.Display}, Java: {javaw}");
        progress?.Report(new("Запуск игры…", 1));
        proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        return new GameProcess(proc, logPath);
    }
}
