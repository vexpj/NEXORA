using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Core.Packs;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Mods;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Packs;

public sealed record PackExportResult(int Exported, IReadOnlyList<string> SkippedManual, int ConfigFiles);

/// <summary>Экспорт и импорт сборок. В файл попадают только ссылки и контрольные суммы модов (не сами моды) и, по желанию, безопасные конфиги.</summary>
public sealed class PackService
{
    public const string Extension = ".nexora-pack";
    private const int MaxConfigFileBytes = 512 * 1024;
    private const long MaxConfigTotalBytes = 20L * 1024 * 1024;
    private const int MaxZipEntries = 5000;
    private static readonly Regex SensitiveName = new(@"(token|secret|password|credential|private|session|account|auth|\.key$|\.pem$|\.log$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly LauncherPaths _paths;
    private readonly ModProvenanceStore _prov;
    private readonly FileDownloader _dl;

    public PackService(LauncherPaths paths, ModProvenanceStore prov, FileDownloader dl) { _paths = paths; _prov = prov; _dl = dl; }

    public static bool IsUrlAllowed(string url)
    {
        try { HostPolicy.Require(url); return true; }
        catch (Exception ex) when (ex is UntrustedHostException or UriFormatException) { return false; }
    }

    public PackManifest BuildManifest(GameProfile p, bool includeConfigs, out List<string> skippedManual)
    {
        skippedManual = new();
        var m = new PackManifest { Name = p.Name, MinecraftVersion = p.MinecraftVersion, Loader = p.Loader, LoaderVersion = p.LoaderVersion };
        var dir = _paths.ModsDir(p.Id);
        var records = _prov.Load(p.Id);
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.jar").OrderBy(x => x))
            {
                var name = Path.GetFileName(f);
                var r = records.FirstOrDefault(x => string.Equals(x.FileName, name, StringComparison.OrdinalIgnoreCase));
                if (r is null || string.IsNullOrEmpty(r.Url) || (r.Sha1 is null && r.Sha512 is null)) { skippedManual.Add(name); continue; }
                m.Mods.Add(new PackMod { Source = r.Source, ProjectId = r.ProjectId, FileName = r.FileName, Url = r.Url, Sha1 = r.Sha1, Sha512 = r.Sha512, Version = r.Version });
            }
        m.HasConfigs = includeConfigs && Directory.Exists(Path.Combine(_paths.InstanceDir(p.Id), "config"));
        return m;
    }

    public PackExportResult ExportFile(GameProfile p, string destPath, bool includeConfigs)
    {
        var manifest = BuildManifest(p, includeConfigs, out var skipped);
        var configFiles = includeConfigs ? EnumerateSafeConfigs(p).ToList() : new List<(string full, string rel)>();
        manifest.HasConfigs = configFiles.Count > 0;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);
        var tmp = destPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("pack.json", CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                w.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(PackCodec.Json) { WriteIndented = true }));
            foreach (var (full, rel) in configFiles) zip.CreateEntryFromFile(full, "config/" + rel, CompressionLevel.Optimal);
        }
        File.Move(tmp, destPath, overwrite: true);
        return new PackExportResult(manifest.Mods.Count, skipped, configFiles.Count);
    }

    public string ExportCode(GameProfile p, out List<string> skippedManual) => PackCodec.Encode(BuildManifest(p, false, out skippedManual));

    private IEnumerable<(string full, string rel)> EnumerateSafeConfigs(GameProfile p)
    {
        var root = Path.Combine(_paths.InstanceDir(p.Id), "config");
        if (!Directory.Exists(root)) yield break;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var fi = new FileInfo(f);
            if (fi.Length > MaxConfigFileBytes || fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (SensitiveName.IsMatch(rel)) continue;   // ключи и токены в конфиги чужим людям не отдаём
            total += fi.Length;
            if (total > MaxConfigTotalBytes) yield break;
            yield return (f, rel);
        }
    }

    // ---------- чтение ----------
    public PackManifest ReadFile(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > MaxZipEntries) throw new InvalidDataException("В файле сборки слишком много элементов.");
            var e = zip.GetEntry("pack.json") ?? throw new InvalidDataException("Это не файл сборки NEXORA (нет pack.json).");
            if (e.Length > 2 * 1024 * 1024) throw new InvalidDataException("Описание сборки слишком большое.");
            using var s = e.Open();
            var m = JsonSerializer.Deserialize<PackManifest>(s, PackCodec.Json) ?? throw new InvalidDataException("Пустая сборка.");
            PackValidator.Validate(m, IsUrlAllowed);
            return m;
        }
        catch (JsonException) { throw new InvalidDataException("Файл сборки повреждён."); }
        catch (InvalidDataException) { throw; }
        catch (IOException ex) { throw new InvalidDataException("Не удалось прочитать файл сборки: " + ex.Message); }
    }

    public PackManifest ReadCode(string code)
    {
        var m = PackCodec.Decode(code);
        PackValidator.Validate(m, IsUrlAllowed);
        return m;
    }

    // ---------- установка ----------
    /// <summary>Создаёт новый профиль и скачивает моды с проверкой контрольных сумм. При ошибке новая папка удаляется.</summary>
    public async Task<GameProfile> ImportAsync(PackManifest m, string? zipPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        PackValidator.Validate(m, IsUrlAllowed);
        var profile = new GameProfile { Name = m.Name.Trim(), MinecraftVersion = m.MinecraftVersion, Loader = m.Loader, LoaderVersion = m.LoaderVersion };
        var instance = _paths.InstanceDir(profile.Id);
        var modsDir = _paths.ModsDir(profile.Id);
        Directory.CreateDirectory(modsDir);
        try
        {
            var done = 0;
            await Parallel.ForEachAsync(m.Mods, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (mod, t) =>
            {
                var name = ModInstaller.SafeFileName(mod.FileName);
                var dest = GameInstaller.SafeCombine(modsDir, name);
                var hash = mod.Sha512 is not null ? new FileHash("sha512", mod.Sha512) : new FileHash("sha1", mod.Sha1!);
                await _dl.DownloadAsync(mod.Url, dest, hash, t);
                _prov.Upsert(profile.Id, new ModRecord { FileName = name, Source = mod.Source, ProjectId = mod.ProjectId, Version = mod.Version, Url = mod.Url, Sha1 = mod.Sha1, Sha512 = mod.Sha512 });
                var n = Interlocked.Increment(ref done);
                progress?.Report(new($"Моды: {n}/{m.Mods.Count} — {name}", (double)n / Math.Max(1, m.Mods.Count)));
            });
            if (zipPath is not null && m.HasConfigs) ExtractConfigs(zipPath, instance);
            return profile;
        }
        catch
        {
            try { if (Directory.Exists(instance)) Directory.Delete(instance, recursive: true); } catch (IOException) { }
            throw;
        }
    }

    private static void ExtractConfigs(string zipPath, string instanceDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith("config/", StringComparison.Ordinal) || e.FullName.EndsWith('/')) continue;
            if (e.Length > MaxConfigFileBytes) continue;
            total += e.Length;
            if (total > MaxConfigTotalBytes) throw new InvalidDataException("Конфигурации в сборке слишком большие.");
            var dest = GameInstaller.SafeCombine(instanceDir, e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }
}
