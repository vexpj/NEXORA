using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Loaders;

/// <summary>Версии и установка Fabric, Forge и NeoForge.</summary>
public sealed class LoaderService
{
    private readonly LauncherPaths _paths;
    private readonly SafeHttp _http;
    private readonly FileDownloader _dl;
    private readonly GameInstaller _installer;
    private readonly JavaService _java;

    public LoaderService(LauncherPaths paths, SafeHttp http, FileDownloader dl, GameInstaller installer, JavaService java)
    { _paths = paths; _http = http; _dl = dl; _installer = installer; _java = java; }

    // ---------- Список версий ----------
    public async Task<IReadOnlyList<string>> GetVersionsAsync(LoaderType type, string mc, CancellationToken ct)
    {
        switch (type)
        {
            case LoaderType.Fabric:
                var fj = JsonNode.Parse(await _http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(mc)}", ct)) as JsonArray;
                return fj?.Select(x => x?["loader"]?["version"]?.GetValue<string>()).Where(x => x is not null).Select(x => x!).ToList() ?? new();
            case LoaderType.Forge:
                var xml = XDocument.Parse(await _http.GetStringAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", ct));
                return ParseForgeVersions(xml.Descendants("version").Select(e => e.Value), mc);
            case LoaderType.NeoForge:
                var nj = JsonNode.Parse(await _http.GetStringAsync("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct));
                return ParseNeoForgeVersions((nj?["versions"] as JsonArray)?.Select(x => x!.GetValue<string>()) ?? Array.Empty<string>(), mc);
            default: return Array.Empty<string>();
        }
    }

    /// <summary>В maven-metadata Forge версии вида «1.20.1-47.2.0»; возвращаем «47.2.0», новые первыми.</summary>
    public static List<string> ParseForgeVersions(IEnumerable<string> all, string mc) =>
        all.Where(v => v.StartsWith(mc + "-", StringComparison.Ordinal)).Select(v => v[(mc.Length + 1)..]).Reverse().ToList();

    /// <summary>NeoForge: Minecraft 1.A.B ↔ версии «A.B.x» (1.21 → «21.0.x»).</summary>
    public static List<string> ParseNeoForgeVersions(IEnumerable<string> all, string mc)
    {
        var p = mc.Split('.');
        if (p.Length < 2 || p[0] != "1") return new();
        var prefix = $"{p[1]}.{(p.Length > 2 ? p[2] : "0")}.";
        return all.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).Reverse().ToList();
    }

    public static string? PickDefault(IReadOnlyList<string> versions) =>
        versions.FirstOrDefault(v => !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();

    // ---------- Установка ----------
    public async Task<string> InstallAsync(GameProfile p, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var version = p.LoaderVersion;
        if (string.IsNullOrEmpty(version))
        {
            var list = await GetVersionsAsync(p.Loader, p.MinecraftVersion, ct);
            version = PickDefault(list) ?? throw new InvalidOperationException($"Для Minecraft {p.MinecraftVersion} нет версий {p.Loader}.");
            p.LoaderVersion = version;
        }
        return p.Loader switch
        {
            LoaderType.Fabric => await InstallFabricAsync(p.MinecraftVersion, version, ct),
            LoaderType.Forge => await InstallInstallerBasedAsync(
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{p.MinecraftVersion}-{version}/forge-{p.MinecraftVersion}-{version}-installer.jar",
                p.MinecraftVersion, "forge", $"{p.MinecraftVersion}-forge-{version}", progress, ct),
            LoaderType.NeoForge => await InstallInstallerBasedAsync(
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar",
                p.MinecraftVersion, "neoforge", $"neoforge-{version}", progress, ct),
            _ => throw new InvalidOperationException("Для Vanilla загрузчик не нужен."),
        };
    }

    private async Task<string> InstallFabricAsync(string mc, string loader, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(mc)}/{Uri.EscapeDataString(loader)}/profile/json", ct);
        var id = VersionInfoParser.Parse(json).Id;
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Недопустимый id версии Fabric.");
        Directory.CreateDirectory(_paths.VersionDir(id));
        await File.WriteAllTextAsync(_paths.VersionJson(id), json, ct);
        return id;
    }

    /// <summary>Forge/NeoForge: скачиваем официальный установщик, сверяем SHA-1 с maven и запускаем его без окна.</summary>
    private async Task<string> InstallInstallerBasedAsync(string installerUrl, string mc, string marker, string expectedId, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var tmpDir = Path.Combine(_paths.DataDir, "tmp");
        Directory.CreateDirectory(tmpDir);
        var jar = Path.Combine(tmpDir, Path.GetFileName(new Uri(installerUrl).AbsolutePath));

        progress?.Report(new($"Загрузка установщика {marker}…", 0.1));
        var sha1 = (await _http.GetStringAsync(installerUrl + ".sha1", ct)).Trim().Split(' ', '\n', '\r')[0];
        await _dl.DownloadAsync(installerUrl, jar, new FileHash("sha1", sha1), ct);

        var vanilla = await _installer.ResolveAsync(mc, ct);
        var javaw = await _java.EnsureRuntimeAsync(vanilla.JavaComponent ?? "jre-legacy", progress, ct);
        var javaExe = Path.Combine(Path.GetDirectoryName(javaw)!, "java.exe");
        if (!File.Exists(javaExe)) javaExe = javaw;

        Directory.CreateDirectory(_paths.Root);
        var lp = Path.Combine(_paths.Root, "launcher_profiles.json");
        if (!File.Exists(lp)) await File.WriteAllTextAsync(lp, "{\"profiles\":{}}", ct);

        var before = new HashSet<string>(Directory.Exists(_paths.Versions) ? Directory.GetDirectories(_paths.Versions) : Array.Empty<string>());
        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = _paths.Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-jar"); psi.ArgumentList.Add(jar);
        psi.ArgumentList.Add("--installClient"); psi.ArgumentList.Add(_paths.Root);

        var tail = new Queue<string>();
        using var proc = new Process { StartInfo = psi };
        void OnLine(string? l)
        {
            if (string.IsNullOrWhiteSpace(l)) return;
            lock (tail) { tail.Enqueue(l); if (tail.Count > 15) tail.Dequeue(); }
            progress?.Report(new($"Установка {marker}: {l}", 0.5));
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        using (ct.Register(() => { try { proc.Kill(true); } catch (InvalidOperationException) { } }))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            await proc.WaitForExitAsync(timeout.Token);
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Установщик {marker} завершился с ошибкой ({proc.ExitCode}):\n" + string.Join('\n', tail));

        if (File.Exists(_paths.VersionJson(expectedId))) return expectedId;
        var created = Directory.GetDirectories(_paths.Versions).Where(d => !before.Contains(d))
            .Select(Path.GetFileName).Where(n => n is not null && n.Contains(marker, StringComparison.OrdinalIgnoreCase) && File.Exists(_paths.VersionJson(n!))).ToList();
        return created.FirstOrDefault() ?? throw new InvalidOperationException($"Установщик {marker} не создал версию в папке versions.");
    }
}
