using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NEXORA.Core.Models;

namespace NEXORA.Core.Packs;

public sealed class PackMod
{
    public string Source { get; set; } = "";       // Modrinth | CurseForge
    public string ProjectId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public string? Sha512 { get; set; }
    public string? Version { get; set; }
}

/// <summary>Описание сборки для обмена. Содержит только ссылки и контрольные суммы, а не сами файлы модов.</summary>
public sealed class PackManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; }
    public string? LoaderVersion { get; set; }
    public List<PackMod> Mods { get; set; } = new();
    public bool HasConfigs { get; set; }
}

public static class PackValidator
{
    public const int MaxMods = 500;
    private static readonly Regex Ver = new(@"^[0-9A-Za-z._+\-]{1,60}$", RegexOptions.Compiled);
    private static readonly Regex Sha1 = new("^[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private static readonly Regex Sha512 = new("^[0-9a-fA-F]{128}$", RegexOptions.Compiled);

    /// <summary>Манифест приходит от чужого человека, поэтому проверяется строго. Бросает InvalidDataException с понятным текстом.</summary>
    public static void Validate(PackManifest m, Func<string, bool> isUrlAllowed)
    {
        if (m.FormatVersion != 1) throw new InvalidDataException("Эта сборка создана несовместимой версией NEXORA.");
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 60 || m.Name.Any(char.IsControl))
            throw new InvalidDataException("Некорректное название сборки.");
        if (!Ver.IsMatch(m.MinecraftVersion ?? "")) throw new InvalidDataException("Некорректная версия Minecraft в сборке.");
        if (m.LoaderVersion is not null && !Ver.IsMatch(m.LoaderVersion)) throw new InvalidDataException("Некорректная версия загрузчика в сборке.");
        if (!Enum.IsDefined(m.Loader)) throw new InvalidDataException("Неизвестный загрузчик модов.");
        if (m.Mods.Count > MaxMods) throw new InvalidDataException($"Слишком много модов (больше {MaxMods}).");
        foreach (var mod in m.Mods)
        {
            if (mod.Source is not ("Modrinth" or "CurseForge")) throw new InvalidDataException("Неизвестный источник мода: " + Trim(mod.Source));
            var f = mod.FileName ?? "";
            if (f.Length is 0 or > 200 || f.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || f.Contains("..") || !f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Некорректное имя файла мода: " + Trim(f));
            if (!isUrlAllowed(mod.Url ?? "")) throw new InvalidDataException($"Мод «{Trim(f)}» предлагает загрузку с недоверенного адреса. Сборка отклонена.");
            var okSha = (mod.Sha1 is not null && Sha1.IsMatch(mod.Sha1)) || (mod.Sha512 is not null && Sha512.IsMatch(mod.Sha512));
            if (!okSha) throw new InvalidDataException($"У мода «{Trim(f)}» нет контрольной суммы — установка небезопасна, сборка отклонена.");
        }
    }

    private static string Trim(string s) => s.Length > 60 ? s[..60] + "…" : s;
}

/// <summary>Код сборки: «NEXORA1:» + base64url(deflate(json)). Подходит для отправки текстом.</summary>
public static class PackCodec
{
    public const string Prefix = "NEXORA1:";
    private const int MaxInflated = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static string Encode(PackManifest m)
    {
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m, Json));
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) ds.Write(raw);
        return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static PackManifest Decode(string code)
    {
        code = (code ?? "").Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Это не код сборки NEXORA.");
        var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        byte[] compressed;
        try { compressed = Convert.FromBase64String(b64); }
        catch (FormatException) { throw new InvalidDataException("Код сборки повреждён."); }
        using var ds = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var limited = new MemoryStream();
        var buf = new byte[8192]; int n;
        try
        {
            while ((n = ds.Read(buf, 0, buf.Length)) > 0)
            {
                limited.Write(buf, 0, n);
                if (limited.Length > MaxInflated) throw new InvalidDataException("Код сборки слишком большой.");
            }
        }
        catch (InvalidDataException) { throw; }
        try { return JsonSerializer.Deserialize<PackManifest>(limited.ToArray(), Json) ?? throw new InvalidDataException("Пустая сборка."); }
        catch (JsonException) { throw new InvalidDataException("Код сборки повреждён."); }
    }
}
