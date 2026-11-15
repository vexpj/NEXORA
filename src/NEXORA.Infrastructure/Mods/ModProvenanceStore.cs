using NEXORA.Core.Minecraft;
using NEXORA.Infrastructure.Storage;

namespace NEXORA.Infrastructure.Mods;

public sealed class ModRecord
{
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? Version { get; set; }
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public string? Sha512 { get; set; }
}

/// <summary>Откуда скачан каждый мод профиля (нужно для обмена сборками).</summary>
public sealed class ModProvenanceStore
{
    private readonly LauncherPaths _paths;
    private readonly object _lock = new();
    public ModProvenanceStore(LauncherPaths paths) => _paths = paths;

    private string FileOf(string profileId) => Path.Combine(_paths.InstanceDir(profileId), "nexora-mods.json");

    public List<ModRecord> Load(string profileId) => JsonFile.Read<List<ModRecord>>(FileOf(profileId)) ?? new();

    public void Upsert(string profileId, ModRecord rec)
    {
        lock (_lock)
        {
            var list = Load(profileId);
            list.RemoveAll(r => string.Equals(r.FileName, rec.FileName, StringComparison.OrdinalIgnoreCase));
            list.Add(rec);
            JsonFile.WriteAtomic(FileOf(profileId), list);
        }
    }

    public void Remove(string profileId, string fileName)
    {
        lock (_lock)
        {
            var list = Load(profileId);
            if (list.RemoveAll(r => string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase)) > 0)
                JsonFile.WriteAtomic(FileOf(profileId), list);
        }
    }
}
