namespace NEXORA.Core.Minecraft;

public sealed record DownloadFile(string Path, string Url, string? Sha1, long Size);

public sealed record Rule(bool Allow, string? OsName, string? OsArch, string? OsVersion, Dictionary<string, bool>? Features);

public sealed class ArgumentEntry
{
    public List<string> Values { get; } = new();
    public List<Rule> Rules { get; } = new();
}

public sealed class LibraryInfo
{
    public string Name { get; set; } = "";
    public DownloadFile? Artifact { get; set; }
    /// <summary>Нативная библиотека старого формата (классификатор natives-windows), её нужно распаковать.</summary>
    public DownloadFile? NativeArtifact { get; set; }
    public List<Rule> Rules { get; } = new();
    public List<string> ExtractExcludes { get; } = new();
    /// <summary>group:artifact[:classifier] без версии — ключ для устранения дублей.</summary>
    public string Key
    {
        get
        {
            var p = Name.Split('@')[0].Split(':');
            return p.Length >= 4 ? $"{p[0]}:{p[1]}:{p[3]}" : p.Length >= 2 ? $"{p[0]}:{p[1]}" : Name;
        }
    }
}

public sealed record AssetIndexInfo(string Id, string Url, string? Sha1, long Size);

public sealed class VersionInfo
{
    public string Id { get; set; } = "";
    public string? InheritsFrom { get; set; }
    public string? Jar { get; set; }
    public string MainClass { get; set; } = "";
    public string Type { get; set; } = "release";
    public string? MinecraftArguments { get; set; }
    public List<ArgumentEntry> GameArgs { get; } = new();
    public List<ArgumentEntry> JvmArgs { get; } = new();
    public List<LibraryInfo> Libraries { get; } = new();
    public AssetIndexInfo? AssetIndex { get; set; }
    public string? Assets { get; set; }
    public DownloadFile? ClientJar { get; set; }
    public int JavaMajor { get; set; }
    public string? JavaComponent { get; set; }
    public string? LoggingArgument { get; set; }
    public DownloadFile? LoggingFile { get; set; }
    /// <summary>Id версии, из которой берётся client.jar (после слияния с родителем).</summary>
    public string JarId { get; set; } = "";
}

public sealed record VersionManifestEntry(string Id, string Type, string Url, string? Sha1, DateTimeOffset ReleaseTime);
