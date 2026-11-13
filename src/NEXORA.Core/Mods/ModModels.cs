using NEXORA.Core.Models;

namespace NEXORA.Core.Mods;

public sealed class ModProject
{
    public string Source { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Author { get; set; } = "";
    public long Downloads { get; set; }
    public string? IconUrl { get; set; }
    public string? PageUrl { get; set; }
    public string DownloadsText => Downloads >= 1_000_000 ? $"{Downloads / 1_000_000.0:0.#} млн" : Downloads >= 1000 ? $"{Downloads / 1000.0:0.#} тыс." : Downloads.ToString();
}

public sealed class ModFile
{
    public string Source { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public string? Sha1 { get; set; }
    public string? Sha512 { get; set; }
    public long Size { get; set; }
    public string Version { get; set; } = "";
    public List<string> RequiredDependencies { get; set; } = new();
}

public interface IModCatalog
{
    string Name { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    Task<IReadOnlyList<ModProject>> SearchAsync(string query, string mcVersion, LoaderType loader, int offset, CancellationToken ct);
    Task<ModFile?> GetBestFileAsync(string projectId, string mcVersion, LoaderType loader, CancellationToken ct);
}

public sealed record InstalledMod(string FileName, bool Enabled, string? Title, string? Version);
