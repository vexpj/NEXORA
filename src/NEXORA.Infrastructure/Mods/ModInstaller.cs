using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Mods;

public sealed class ModInstaller
{
    private readonly LauncherPaths _paths;
    private readonly FileDownloader _dl;
    public ModInstaller(LauncherPaths paths, FileDownloader dl) { _paths = paths; _dl = dl; }

    public static string SafeFileName(string name)
    {
        var n = Path.GetFileName(name.Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        if (!n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Разрешены только файлы .jar: " + name);
        return n;
    }

    /// <summary>Скачивает мод (и обязательные зависимости) в папку mods профиля с проверкой контрольной суммы.</summary>
    public async Task<List<string>> InstallAsync(GameProfile profile, IModCatalog catalog, ModFile file, CancellationToken ct, int depth = 0)
    {
        if (string.IsNullOrEmpty(file.DownloadUrl))
            throw new InvalidOperationException("Автор запретил скачивание вне сайта CurseForge. Скачайте мод вручную на его странице.");
        var dir = _paths.ModsDir(profile.Id);
        Directory.CreateDirectory(dir);
        var name = SafeFileName(file.FileName);
        var dest = Path.Combine(dir, name);
        var hash = file.Sha512 is not null ? new FileHash("sha512", file.Sha512) : file.Sha1 is not null ? new FileHash("sha1", file.Sha1) : null;
        await _dl.DownloadAsync(file.DownloadUrl, dest, hash, ct);
        if (File.Exists(dest + ".disabled")) File.Delete(dest + ".disabled");

        var installed = new List<string> { name };
        if (depth < 2)
            foreach (var dep in file.RequiredDependencies)
            {
                var depFile = await catalog.GetBestFileAsync(dep, profile.MinecraftVersion, profile.Loader, ct);
                if (depFile is null || string.IsNullOrEmpty(depFile.DownloadUrl)) continue;
                var depName = SafeFileName(depFile.FileName);
                if (File.Exists(Path.Combine(dir, depName)) || File.Exists(Path.Combine(dir, depName + ".disabled"))) continue;
                installed.AddRange(await InstallAsync(profile, catalog, depFile, ct, depth + 1));
            }
        return installed;
    }

    public List<InstalledMod> List(GameProfile profile)
    {
        var dir = _paths.ModsDir(profile.Id);
        if (!Directory.Exists(dir)) return new();
        var result = new List<InstalledMod>();
        foreach (var f in Directory.GetFiles(dir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
        {
            var (title, ver) = ReadMeta(f);
            result.Add(new InstalledMod(Path.GetFileName(f), !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), title, ver));
        }
        return result;
    }

    public void SetEnabled(GameProfile profile, string fileName, bool enabled)
    {
        var dir = _paths.ModsDir(profile.Id);
        var cur = GameInstaller_SafePath(dir, fileName);
        var target = enabled ? cur.Replace(".jar.disabled", ".jar", StringComparison.OrdinalIgnoreCase)
                             : (cur.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? cur : cur + ".disabled");
        if (cur != target) File.Move(cur, target, overwrite: true);
    }

    public void Delete(GameProfile profile, string fileName) => File.Delete(GameInstaller_SafePath(_paths.ModsDir(profile.Id), fileName));

    private static string GameInstaller_SafePath(string dir, string fileName) =>
        NEXORA.Infrastructure.Minecraft.GameInstaller.SafeCombine(dir, Path.GetFileName(fileName));

    private static (string? title, string? version) ReadMeta(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("fabric.mod.json") is { } fe)
            {
                using var r = new StreamReader(fe.Open());
                var j = JsonNode.Parse(r.ReadToEnd());
                return (j?["name"]?.GetValue<string>() ?? j?["id"]?.GetValue<string>(), j?["version"]?.GetValue<string>());
            }
            var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
            if (toml is not null)
            {
                using var r = new StreamReader(toml.Open());
                var t = r.ReadToEnd();
                var name = Regex.Match(t, "displayName\\s*=\\s*\"([^\"]+)\"");
                var ver = Regex.Match(t, "(?m)^\\s*version\\s*=\\s*\"([^\"$]+)\"");
                return (name.Success ? name.Groups[1].Value : null, ver.Success ? ver.Groups[1].Value : null);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException) { }
        return (null, null);
    }
}
