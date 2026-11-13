using System.IO.Compression;
using System.Text.Json.Nodes;
using NEXORA.Core.Minecraft;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Minecraft;

public sealed record InstallProgress(string Stage, double Fraction);

public sealed class InstalledVersion
{
    public required VersionInfo Info { get; init; }
    public required string LaunchId { get; init; }
    public required List<string> Classpath { get; init; }
    public required string NativesDir { get; init; }
    public string? LoggingConfigPath { get; init; }
    public required string AssetsIndexName { get; init; }
    public required string GameAssetsDir { get; init; }
}

public sealed class GameInstaller
{
    private readonly LauncherPaths _paths;
    private readonly VersionService _versions;
    private readonly SafeHttp _http;
    private readonly FileDownloader _dl;

    public GameInstaller(LauncherPaths paths, VersionService versions, SafeHttp http, FileDownloader dl)
    { _paths = paths; _versions = versions; _http = http; _dl = dl; }

    public async Task<VersionInfo> ResolveAsync(string id, CancellationToken ct)
    {
        var info = await _versions.LoadAsync(id, ct);
        if (info.InheritsFrom is { } parentId)
            return VersionMerger.Merge(info, await ResolveAsync(parentId, ct));
        return info;
    }

    public async Task<InstalledVersion> InstallAsync(string id, string? gameDir, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("Чтение описания версии…", 0));
        var info = await ResolveAsync(id, ct);

        // 1. client.jar
        var jarPath = _paths.VersionJar(info.JarId);
        if (info.ClientJar is { } cj)
        {
            progress?.Report(new("Загрузка Minecraft…", 0.02));
            await _dl.DownloadAsync(cj.Url, jarPath, cj.Sha1 is null ? null : new FileHash("sha1", cj.Sha1), ct);
        }
        else if (!File.Exists(jarPath))
            throw new FileNotFoundException($"Не найден client.jar версии {info.JarId}.");

        // 2. библиотеки
        var libs = info.Libraries.Where(l => RuleEvaluator.IsAllowed(l.Rules)).ToList();
        var files = libs.SelectMany(l => new[] { l.Artifact, l.NativeArtifact }).Where(f => f is not null).Select(f => f!).DistinctBy(f => f.Path).ToList();
        var done = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (f, t) =>
        {
            var dest = SafeCombine(_paths.Libraries, f.Path);
            if (string.IsNullOrEmpty(f.Url))
            {
                if (!File.Exists(dest)) throw new FileNotFoundException($"Библиотека отсутствует и не имеет адреса загрузки: {f.Path}");
            }
            else await _dl.DownloadAsync(f.Url, dest, f.Sha1 is null ? null : new FileHash("sha1", f.Sha1), t);
            var n = Interlocked.Increment(ref done);
            progress?.Report(new($"Библиотеки: {n}/{files.Count}", 0.05 + 0.30 * n / Math.Max(1, files.Count)));
        });

        // 3. ресурсы
        var (assetsName, gameAssets) = await InstallAssetsAsync(info, gameDir, progress, ct);

        // 4. конфиг логирования (защита от Log4Shell в старых версиях)
        string? logCfg = null;
        if (info.LoggingFile is { } lf)
        {
            logCfg = SafeCombine(Path.Combine(_paths.Assets), lf.Path);
            await _dl.DownloadAsync(lf.Url, logCfg, lf.Sha1 is null ? null : new FileHash("sha1", lf.Sha1), ct);
        }

        // 5. нативные библиотеки (старый формат)
        var natives = Path.Combine(_paths.VersionDir(id), "natives");
        if (Directory.Exists(natives)) Directory.Delete(natives, true);
        Directory.CreateDirectory(natives);
        foreach (var l in libs.Where(l => l.NativeArtifact is not null))
            ExtractNatives(SafeCombine(_paths.Libraries, l.NativeArtifact!.Path), natives, l.ExtractExcludes);

        var classpath = libs.Where(l => l.Artifact is not null).Select(l => SafeCombine(_paths.Libraries, l.Artifact!.Path)).Distinct().ToList();
        classpath.Add(jarPath);
        progress?.Report(new("Готово", 1));

        return new InstalledVersion
        {
            Info = info, LaunchId = id, Classpath = classpath, NativesDir = natives, LoggingConfigPath = logCfg,
            AssetsIndexName = assetsName, GameAssetsDir = gameAssets,
        };
    }

    private async Task<(string name, string gameAssets)> InstallAssetsAsync(VersionInfo info, string? gameDir, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        if (info.AssetIndex is not { } ai) return (info.Assets ?? "legacy", _paths.Assets);
        var indexPath = Path.Combine(_paths.Assets, "indexes", ai.Id + ".json");
        await _dl.DownloadAsync(ai.Url, indexPath, ai.Sha1 is null ? null : new FileHash("sha1", ai.Sha1), ct);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, ct))!.AsObject();
        var isVirtual = root["virtual"]?.GetValue<bool>() ?? false;
        var mapToResources = root["map_to_resources"]?.GetValue<bool>() ?? false;
        var objects = (root["objects"] as JsonObject)?.Select(kv => (Name: kv.Key, Hash: kv.Value!["hash"]!.GetValue<string>(), Size: kv.Value["size"]!.GetValue<long>())).ToList()
                      ?? new();

        var done = 0;
        await Parallel.ForEachAsync(objects, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct }, async (o, t) =>
        {
            var sub = o.Hash[..2];
            var dest = Path.Combine(_paths.Assets, "objects", sub, o.Hash);
            if (!(File.Exists(dest) && new FileInfo(dest).Length == o.Size))
                await _dl.DownloadAsync($"https://resources.download.minecraft.net/{sub}/{o.Hash}", dest, new FileHash("sha1", o.Hash), t);
            var n = Interlocked.Increment(ref done);
            if (n % 50 == 0 || n == objects.Count)
                progress?.Report(new($"Ресурсы: {n}/{objects.Count}", 0.35 + 0.60 * n / Math.Max(1, objects.Count)));
        });

        var gameAssets = _paths.Assets;
        if (isVirtual || (mapToResources && gameDir is not null))
        {
            var target = isVirtual ? Path.Combine(_paths.Assets, "virtual", "legacy") : Path.Combine(gameDir!, "resources");
            foreach (var o in objects)
            {
                var dest = SafeCombine(target, o.Name);
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(_paths.Assets, "objects", o.Hash[..2], o.Hash), dest);
            }
            if (isVirtual) gameAssets = target;
        }
        return (ai.Id, gameAssets);
    }

    private static void ExtractNatives(string jar, string targetDir, List<string> excludes)
    {
        using var zip = ZipFile.OpenRead(jar);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/') || e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
            if (excludes.Any(x => e.FullName.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
            var dest = SafeCombine(targetDir, e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    /// <summary>Защита от «../» в путях из внешних JSON и архивов.</summary>
    public static string SafeCombine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Недопустимый путь в данных загрузки: " + relative);
        return full;
    }
}
