namespace NEXORA.Core.Minecraft;

/// <summary>Все пути лаунчера в одном месте.</summary>
public sealed class LauncherPaths
{
    public LauncherPaths(string root, string dataDir) { Root = root; DataDir = dataDir; }

    public string Root { get; }          // папка Minecraft
    public string DataDir { get; }       // настройки, профили, аккаунт, логи лаунчера
    public string Versions => Path.Combine(Root, "versions");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Assets => Path.Combine(Root, "assets");
    public string Runtime => Path.Combine(Root, "runtime");
    public string Instances => Path.Combine(Root, "instances");
    public string LogsDir => Path.Combine(DataDir, "logs");

    public string VersionDir(string id) => Path.Combine(Versions, id);
    public string VersionJson(string id) => Path.Combine(VersionDir(id), id + ".json");
    public string VersionJar(string id) => Path.Combine(VersionDir(id), id + ".jar");
    public string InstanceDir(string profileId) => Path.Combine(Instances, profileId);
    public string ModsDir(string profileId) => Path.Combine(InstanceDir(profileId), "mods");
}
