using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NEXORA.Core.Updates;

/// <summary>Версия лаунчера берётся из сборки (единый источник — Directory.Build.props).</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();
    public static Version Parsed => Version.TryParse(Current, out var v) ? v : new Version(0, 0, 0);

    private static string Read()
    {
        var asm = typeof(AppVersion).Assembly;
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}

public static class UpdateConfig
{
    /// <summary>Репозиторий с релизами. Для обновлений он должен быть публичным (или релизы — в отдельном публичном репозитории).</summary>
    public const string Repository = "vexpj/NEXORA";
    public const string SetupAsset = "NEXORA-Setup.exe";
    public const string HashAsset = "NEXORA-Setup.exe.sha256";
    /// <summary>Когда у установщика появится цифровая подпись, поставьте true: неподписанные обновления будут отклоняться.</summary>
    public const bool RequireSignedInstaller = false;
    public const long MaxInstallerBytes = 400L * 1024 * 1024;
}

public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string SetupUrl, string HashUrl);

public static class ReleaseParser
{
    private static readonly Regex Hex64 = new("\\b[0-9a-fA-F]{64}\\b", RegexOptions.Compiled);

    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        return Version.TryParse(t, out var v) ? v : null;
    }

    public static bool IsNewer(Version latest, Version current) => latest > current;

    /// <summary>Разбирает ответ GitHub «latest release». Возвращает null, если релиз не подходит (черновик, нет файлов, чужие адреса).</summary>
    public static ReleaseInfo? Parse(string json, string repository)
    {
        var n = JsonNode.Parse(json) as JsonObject;
        if (n is null) return null;
        if (n["draft"]?.GetValue<bool>() == true || n["prerelease"]?.GetValue<bool>() == true) return null;
        var tag = n["tag_name"]?.GetValue<string>();
        var ver = ParseTag(tag);
        if (ver is null) return null;

        var prefix = $"https://github.com/{repository}/releases/download/";
        string? setup = null, hash = null;
        foreach (var a in (n["assets"] as JsonArray) ?? new JsonArray())
        {
            var name = a?["name"]?.GetValue<string>();
            var url = a?["browser_download_url"]?.GetValue<string>();
            if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) continue;   // только файлы этого релиза этого репозитория
            if (name == UpdateConfig.SetupAsset) setup = url;
            else if (name == UpdateConfig.HashAsset) hash = url;
        }
        if (setup is null || hash is null) return null;
        var notes = n["body"]?.GetValue<string>()?.Trim() ?? "";
        if (notes.Length > 600) notes = notes[..600] + "…";
        return new ReleaseInfo(ver, tag!, notes, setup, hash);
    }

    /// <summary>Файл .sha256: «&lt;хэш&gt;  NEXORA-Setup.exe» или просто хэш.</summary>
    public static string? ParseSha256File(string text, string fileName)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Hex64.Match(line);
            if (!m.Success) continue;
            var rest = line[(m.Index + m.Length)..].Trim().TrimStart('*');
            if (rest.Length > 0 && !string.Equals(rest, fileName, StringComparison.OrdinalIgnoreCase)) continue;
            return m.Value.ToLowerInvariant();
        }
        return null;
    }
}
