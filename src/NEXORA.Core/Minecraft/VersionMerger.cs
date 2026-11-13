namespace NEXORA.Core.Minecraft;

public static class VersionMerger
{
    /// <summary>Накладывает дочернюю версию (Fabric/Forge) на родителя (ванильную).</summary>
    public static VersionInfo Merge(VersionInfo child, VersionInfo parent)
    {
        var m = new VersionInfo
        {
            Id = child.Id,
            InheritsFrom = null,
            Jar = child.Jar,
            MainClass = string.IsNullOrEmpty(child.MainClass) ? parent.MainClass : child.MainClass,
            Type = child.Type,
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Assets = child.Assets ?? parent.Assets,
            ClientJar = child.ClientJar ?? parent.ClientJar,
            JavaMajor = child.JavaMajor != 0 ? child.JavaMajor : parent.JavaMajor,
            JavaComponent = child.JavaComponent ?? parent.JavaComponent,
            LoggingArgument = child.LoggingArgument ?? parent.LoggingArgument,
            LoggingFile = child.LoggingFile ?? parent.LoggingFile,
            JarId = child.Jar ?? parent.JarId,
        };
        // Библиотеки ребёнка важнее; одинаковые group:artifact родителя отбрасываем.
        var seen = new HashSet<string>();
        foreach (var l in child.Libraries.Concat(parent.Libraries))
            if (seen.Add(l.Key + "|" + (l.NativeArtifact is null ? "a" : "n"))) m.Libraries.Add(l);

        m.GameArgs.AddRange(parent.GameArgs); m.GameArgs.AddRange(child.GameArgs);
        m.JvmArgs.AddRange(parent.JvmArgs); m.JvmArgs.AddRange(child.JvmArgs);
        return m;
    }
}
