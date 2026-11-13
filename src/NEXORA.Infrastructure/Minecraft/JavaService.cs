using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEXORA.Core.Minecraft;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Minecraft;

public sealed record JavaInstall(string Path, int Major, string Display);

public sealed class JavaService
{
    private const string AllRuntimesUrl = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
    private readonly LauncherPaths _paths;
    private readonly SafeHttp _http;
    private readonly FileDownloader _dl;

    public JavaService(LauncherPaths paths, SafeHttp http, FileDownloader dl) { _paths = paths; _http = http; _dl = dl; }

    /// <summary>"1.8.0_392" → 8, "17.0.9" → 17, "21" → 21.</summary>
    public static int ParseMajor(string output)
    {
        var m = Regex.Match(output, "version \"(\\d+)(?:\\.(\\d+))?");
        if (!m.Success) return 0;
        var first = int.Parse(m.Groups[1].Value);
        return first == 1 && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : first;
    }

    public async Task<int> GetMajorAsync(string javaExe, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(javaExe, "-version")
            { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var err = await p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return ParseMajor(err);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { return 0; }
    }

    /// <summary>Ищет установленные Java в типичных местах Windows.</summary>
    public async Task<List<JavaInstall>> DetectAsync(CancellationToken ct)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddBin(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var exe = Path.Combine(dir, "bin", "javaw.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }
        AddBin(Environment.GetEnvironmentVariable("JAVA_HOME"));
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var e = Path.Combine(p.Trim('"'), "javaw.exe"); if (File.Exists(e)) candidates.Add(e); } catch (ArgumentException) { }
        }
        var bases = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        var vendors = new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Semeru", "AdoptOpenJDK" };
        foreach (var b in bases.Where(b => !string.IsNullOrEmpty(b)))
            foreach (var v in vendors)
            {
                var vd = Path.Combine(b, v);
                if (!Directory.Exists(vd)) continue;
                foreach (var sub in Directory.GetDirectories(vd)) AddBin(sub);
            }

        var result = new List<JavaInstall>();
        foreach (var exe in candidates)
        {
            var major = await GetMajorAsync(exe, ct);
            if (major > 0) result.Add(new JavaInstall(exe, major, $"Java {major} — {Path.GetDirectoryName(Path.GetDirectoryName(exe))}"));
        }
        return result.OrderByDescending(j => j.Major).ToList();
    }

    /// <summary>Возвращает путь к javaw.exe: выбранная вручную Java или официальная среда Mojang.</summary>
    public async Task<string> ResolveAsync(string? userPath, VersionInfo v, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(userPath))
        {
            if (!File.Exists(userPath)) throw new FileNotFoundException("Выбранная Java не найдена: " + userPath);
            var major = await GetMajorAsync(userPath, ct);
            if (major > 0 && v.JavaMajor > 0 && major < v.JavaMajor)
                throw new InvalidOperationException($"Для этой версии нужна Java {v.JavaMajor} или новее, а выбрана Java {major}. Очистите поле Java в настройках — NEXORA загрузит подходящую.");
            return userPath;
        }
        return await EnsureRuntimeAsync(v.JavaComponent ?? "jre-legacy", progress, ct);
    }

    public async Task<string> EnsureRuntimeAsync(string component, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(_paths.Runtime, component);
        var javaw = Path.Combine(dir, "bin", "javaw.exe");
        var marker = Path.Combine(dir, ".nexora-ok");
        if (File.Exists(marker) && File.Exists(javaw)) return javaw;

        progress?.Report(new($"Загрузка Java ({component})…", 0));
        var all = JsonNode.Parse(await _http.GetStringAsync(AllRuntimesUrl, ct))!;
        var m = all["windows-x64"]?[component]?[0]?["manifest"]
            ?? throw new InvalidOperationException($"Mojang не предоставляет среду Java «{component}» для Windows x64.");
        var manifestUrl = m["url"]!.GetValue<string>();
        var manifestBytes = await DownloadBytesAsync(manifestUrl, ct);
        if (m["sha1"]?.GetValue<string>() is { } expected && !string.Equals(Hashing.Sha1Hex(manifestBytes), expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Манифест Java повреждён (контрольная сумма не совпала).");

        var files = (JsonNode.Parse(manifestBytes)!["files"] as JsonObject)!
            .Where(kv => kv.Value?["type"]?.GetValue<string>() == "file" && kv.Value["downloads"]?["raw"] is not null)
            .Select(kv => (Path: kv.Key, Raw: kv.Value!["downloads"]!["raw"]!)).ToList();

        var done = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (f, t) =>
        {
            var dest = GameInstaller.SafeCombine(dir, f.Path);
            await _dl.DownloadAsync(f.Raw["url"]!.GetValue<string>(), dest, new FileHash("sha1", f.Raw["sha1"]!.GetValue<string>()), t);
            var n = Interlocked.Increment(ref done);
            if (n % 20 == 0 || n == files.Count)
                progress?.Report(new($"Java: {n}/{files.Count}", (double)n / files.Count));
        });
        if (!File.Exists(javaw)) throw new FileNotFoundException("В загруженной среде Java нет javaw.exe.");
        await File.WriteAllTextAsync(marker, DateTime.UtcNow.ToString("O"), ct);
        return javaw;
    }

    private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}
