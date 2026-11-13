using System.Text.Json.Nodes;

namespace NEXORA.Core.Minecraft;

public static class VersionInfoParser
{
    public static VersionInfo Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Пустой JSON версии.");
        var v = new VersionInfo
        {
            Id = Str(root, "id") ?? throw new InvalidDataException("В JSON версии нет id."),
            InheritsFrom = Str(root, "inheritsFrom"),
            Jar = Str(root, "jar"),
            MainClass = Str(root, "mainClass") ?? "",
            Type = Str(root, "type") ?? "release",
            MinecraftArguments = Str(root, "minecraftArguments"),
            Assets = Str(root, "assets"),
        };
        v.JarId = v.Jar ?? v.Id;

        if (root["arguments"] is JsonObject args)
        {
            ParseArgs(args["game"] as JsonArray, v.GameArgs);
            ParseArgs(args["jvm"] as JsonArray, v.JvmArgs);
        }

        if (root["assetIndex"] is JsonObject ai)
            v.AssetIndex = new AssetIndexInfo(Str(ai, "id") ?? "", Str(ai, "url") ?? "", Str(ai, "sha1"), Lng(ai, "size"));
        if (root["downloads"]?["client"] is JsonObject cl && Str(cl, "url") is { } cu)
            v.ClientJar = new DownloadFile("", cu, Str(cl, "sha1"), Lng(cl, "size"));
        if (root["javaVersion"] is JsonObject jv)
        {
            v.JavaComponent = Str(jv, "component");
            v.JavaMajor = (int)Lng(jv, "majorVersion");
        }
        if (root["logging"]?["client"] is JsonObject lg)
        {
            v.LoggingArgument = Str(lg, "argument");
            if (lg["file"] is JsonObject lf && Str(lf, "url") is { } lu && Str(lf, "id") is { } lid)
                v.LoggingFile = new DownloadFile("log_configs/" + lid, lu, Str(lf, "sha1"), Lng(lf, "size"));
        }

        if (root["libraries"] is JsonArray libs)
            foreach (var l in libs.OfType<JsonObject>())
                v.Libraries.Add(ParseLibrary(l));
        return v;
    }

    private static LibraryInfo ParseLibrary(JsonObject l)
    {
        var lib = new LibraryInfo { Name = Str(l, "name") ?? "" };
        ParseRules(l["rules"] as JsonArray, lib.Rules);
        if (l["extract"]?["exclude"] is JsonArray ex)
            foreach (var e in ex) if (e?.GetValue<string>() is { } s) lib.ExtractExcludes.Add(s);

        var dl = l["downloads"] as JsonObject;
        if (dl?["artifact"] is JsonObject art && Str(art, "url") is { } url)
            lib.Artifact = new DownloadFile(Str(art, "path") ?? MavenPath(lib.Name), url, Str(art, "sha1"), Lng(art, "size"));
        else if (dl is null && lib.Name.Length > 0 && l["natives"] is null)
        {
            // Формат Fabric/Forge: только name + необязательный url репозитория.
            var repo = (Str(l, "url") ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/";
            var path = MavenPath(lib.Name);
            lib.Artifact = new DownloadFile(path, repo + path, Str(l, "sha1"), Lng(l, "size"));
        }

        if (l["natives"]?["windows"]?.GetValue<string>() is { } cls)
        {
            cls = cls.Replace("${arch}", "64");
            if (dl?["classifiers"]?[cls] is JsonObject n && Str(n, "url") is { } nu)
                lib.NativeArtifact = new DownloadFile(Str(n, "path") ?? MavenPath(lib.Name, cls), nu, Str(n, "sha1"), Lng(n, "size"));
        }
        return lib;
    }

    private static void ParseArgs(JsonArray? arr, List<ArgumentEntry> into)
    {
        if (arr is null) return;
        foreach (var item in arr)
        {
            var e = new ArgumentEntry();
            if (item is JsonObject o)
            {
                ParseRules(o["rules"] as JsonArray, e.Rules);
                var val = o["value"];
                if (val is JsonArray va) { foreach (var x in va) if (x?.GetValue<string>() is { } s) e.Values.Add(s); }
                else if (val?.GetValue<string>() is { } s1) e.Values.Add(s1);
            }
            else if (item?.GetValue<string>() is { } s) e.Values.Add(s);
            if (e.Values.Count > 0) into.Add(e);
        }
    }

    private static void ParseRules(JsonArray? arr, List<Rule> into)
    {
        if (arr is null) return;
        foreach (var r in arr.OfType<JsonObject>())
        {
            Dictionary<string, bool>? feats = null;
            if (r["features"] is JsonObject f)
            {
                feats = new();
                foreach (var kv in f) feats[kv.Key] = kv.Value?.GetValue<bool>() ?? false;
            }
            var os = r["os"] as JsonObject;
            into.Add(new Rule(Str(r, "action") == "allow", Str(os, "name"), Str(os, "arch"), Str(os, "version"), feats));
        }
    }

    /// <summary>group:artifact:version[:classifier][@ext] → путь вида group/artifact/version/artifact-version[-classifier].ext</summary>
    public static string MavenPath(string name, string? classifierOverride = null)
    {
        var ext = "jar";
        var at = name.IndexOf('@');
        if (at >= 0) { ext = name[(at + 1)..]; name = name[..at]; }
        var p = name.Split(':');
        if (p.Length < 3) throw new InvalidDataException("Неверное имя библиотеки: " + name);
        var cls = classifierOverride ?? (p.Length > 3 ? p[3] : null);
        var file = $"{p[1]}-{p[2]}" + (cls is null ? "" : "-" + cls) + "." + ext;
        return $"{p[0].Replace('.', '/')}/{p[1]}/{p[2]}/{file}";
    }

    private static string? Str(JsonObject? o, string key) =>
        o is not null && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long Lng(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
}
