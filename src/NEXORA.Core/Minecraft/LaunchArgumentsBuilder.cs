namespace NEXORA.Core.Minecraft;

public sealed class LaunchContext
{
    public string PlayerName { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string UserType { get; set; } = "msa";
    public string VersionName { get; set; } = "";
    public string VersionType { get; set; } = "release";
    public string GameDirectory { get; set; } = "";
    public string AssetsRoot { get; set; } = "";
    public string AssetsIndexName { get; set; } = "";
    public string GameAssetsDir { get; set; } = "";
    public string LibraryDirectory { get; set; } = "";
    public string NativesDirectory { get; set; } = "";
    public string? ClientId { get; set; }
    public IReadOnlyList<string> Classpath { get; set; } = Array.Empty<string>();
    public int RamMb { get; set; } = 4096;
    public string? ExtraJvmArgs { get; set; }
    public string? LoggingConfigPath { get; set; }
    public string LauncherName { get; set; } = "NEXORA";
    public string LauncherVersion { get; set; } = "0.2.0";
}

public static class LaunchArgumentsBuilder
{
    /// <summary>Возвращает полный список аргументов для java (без самого исполняемого файла).</summary>
    public static List<string> Build(VersionInfo v, LaunchContext c)
    {
        var map = new Dictionary<string, string>
        {
            ["auth_player_name"] = c.PlayerName,
            ["auth_uuid"] = c.Uuid,
            ["auth_access_token"] = c.AccessToken,
            ["auth_session"] = c.AccessToken,
            ["user_type"] = c.UserType,
            ["user_properties"] = "{}",
            ["version_name"] = c.VersionName,
            ["version_type"] = c.VersionType,
            ["game_directory"] = c.GameDirectory,
            ["assets_root"] = c.AssetsRoot,
            ["assets_index_name"] = c.AssetsIndexName,
            ["game_assets"] = c.GameAssetsDir,
            ["library_directory"] = c.LibraryDirectory,
            ["natives_directory"] = c.NativesDirectory,
            ["classpath"] = string.Join(';', c.Classpath),
            ["classpath_separator"] = ";",
            ["launcher_name"] = c.LauncherName,
            ["launcher_version"] = c.LauncherVersion,
            ["clientid"] = c.ClientId ?? "",
            ["auth_xuid"] = "",
        };

        var result = new List<string> { $"-Xmx{c.RamMb}M", $"-Xms{Math.Min(512, c.RamMb)}M" };
        if (!string.IsNullOrWhiteSpace(c.ExtraJvmArgs))
            result.AddRange(c.ExtraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var jvm = Expand(v.JvmArgs, map);
        if (jvm.Count == 0)
            jvm.AddRange(new[]
            {
                Sub("-Djava.library.path=${natives_directory}", map),
                Sub("-Dminecraft.launcher.brand=${launcher_name}", map),
                Sub("-Dminecraft.launcher.version=${launcher_version}", map),
                "-cp", Sub("${classpath}", map),
            });
        result.AddRange(jvm);

        // Защита от Log4Shell в старых версиях: официальный конфиг логирования Mojang.
        if (v.LoggingArgument is not null && c.LoggingConfigPath is not null)
            result.Add(v.LoggingArgument.Replace("${path}", c.LoggingConfigPath));

        result.Add(v.MainClass);

        if (v.MinecraftArguments is not null && v.GameArgs.Count == 0)
            result.AddRange(v.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => Sub(a, map)));
        else
            result.AddRange(Expand(v.GameArgs, map));
        return result;
    }

    private static List<string> Expand(IEnumerable<ArgumentEntry> entries, Dictionary<string, string> map)
    {
        var list = new List<string>();
        foreach (var e in entries)
            if (RuleEvaluator.IsAllowed(e.Rules))
                list.AddRange(e.Values.Select(x => Sub(x, map)));
        return list;
    }

    private static string Sub(string s, Dictionary<string, string> map)
    {
        foreach (var kv in map) s = s.Replace("${" + kv.Key + "}", kv.Value);
        return s;
    }
}
