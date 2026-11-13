using NEXORA.Core.Auth;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Auth;
using NEXORA.Infrastructure.Loaders;
using NEXORA.Infrastructure.Minecraft;
using Xunit;

namespace NEXORA.Tests;

public class VersionParsingTests
{
    public const string Modern = """
    {"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main","assets":"5",
     "assetIndex":{"id":"5","sha1":"aaa","size":1,"url":"https://piston-meta.mojang.com/v1/packages/x/5.json"},
     "downloads":{"client":{"sha1":"c1","size":10,"url":"https://piston-data.mojang.com/v1/objects/c1/client.jar"}},
     "javaVersion":{"component":"java-runtime-gamma","majorVersion":17},
     "arguments":{
       "game":["--username","${auth_player_name}",{"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"},"--version","${version_name}"],
       "jvm":[{"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"-XX:HeapDumpPath=dump.heapdump"},
              {"rules":[{"action":"allow","os":{"name":"osx"}}],"value":["-XstartOnFirstThread"]},
              "-Djava.library.path=${natives_directory}","-cp","${classpath}"]},
     "libraries":[
       {"name":"com.mojang:logging:1.1.1","downloads":{"artifact":{"path":"com/mojang/logging/1.1.1/logging-1.1.1.jar","sha1":"s1","size":5,"url":"https://libraries.minecraft.net/com/mojang/logging/1.1.1/logging-1.1.1.jar"}}},
       {"name":"org.lwjgl:lwjgl:3.3.1:natives-windows","downloads":{"artifact":{"path":"org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar","sha1":"s2","size":6,"url":"https://libraries.minecraft.net/a.jar"}},"rules":[{"action":"allow","os":{"name":"windows"}}]},
       {"name":"org.lwjgl:lwjgl:3.3.1:natives-windows-x86","downloads":{"artifact":{"path":"org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-x86.jar","sha1":"s3","size":7,"url":"https://libraries.minecraft.net/b.jar"}},"rules":[{"action":"allow","os":{"name":"windows","arch":"x86"}}]}],
     "logging":{"client":{"argument":"-Dlog4j.configurationFile=${path}","file":{"id":"client-1.12.xml","sha1":"l1","size":1,"url":"https://piston-data.mojang.com/v1/objects/l1/client-1.12.xml"}}}}
    """;

    public const string Legacy = """
    {"id":"1.8.9","type":"release","mainClass":"net.minecraft.client.main.Main","assets":"1.8",
     "minecraftArguments":"--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --accessToken ${auth_access_token}",
     "downloads":{"client":{"sha1":"c","size":1,"url":"https://launcher.mojang.com/v1/objects/c/client.jar"}},
     "libraries":[{"name":"org.lwjgl.lwjgl:lwjgl-platform:2.9.4","natives":{"windows":"natives-windows","linux":"natives-linux"},
       "extract":{"exclude":["META-INF/"]},
       "downloads":{"classifiers":{"natives-windows":{"path":"org/lwjgl/lwjgl/lwjgl-platform/2.9.4/lwjgl-platform-2.9.4-natives-windows.jar","sha1":"n1","size":3,"url":"https://libraries.minecraft.net/n.jar"}}}}]}
    """;

    public const string Fabric = """
    {"id":"fabric-loader-0.15.11-1.20.1","inheritsFrom":"1.20.1","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
     "arguments":{"game":[],"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "]},
     "libraries":[{"name":"net.fabricmc:fabric-loader:0.15.11","url":"https://maven.fabricmc.net/","sha1":"f1"},
                  {"name":"com.mojang:logging:1.2.0","url":"https://maven.fabricmc.net/"}]}
    """;

    [Fact]
    public void Modern_ParsesFields()
    {
        var v = VersionInfoParser.Parse(Modern);
        Assert.Equal("1.20.1", v.Id);
        Assert.Equal(17, v.JavaMajor);
        Assert.Equal("java-runtime-gamma", v.JavaComponent);
        Assert.Equal("5", v.AssetIndex!.Id);
        Assert.Equal("c1", v.ClientJar!.Sha1);
        Assert.Equal("logging/client-1.12.xml".Replace("logging/", "log_configs/"), v.LoggingFile!.Path);
        Assert.Equal(3, v.Libraries.Count);
    }

    [Fact]
    public void Libraries_RespectOsAndArch()
    {
        var v = VersionInfoParser.Parse(Modern);
        Assert.True(RuleEvaluator.IsAllowed(v.Libraries[1].Rules));   // natives-windows
        Assert.False(RuleEvaluator.IsAllowed(v.Libraries[2].Rules));  // natives-windows-x86
    }

    [Fact]
    public void Legacy_NativeClassifierIsExtractedNotOnClasspath()
    {
        var lib = VersionInfoParser.Parse(Legacy).Libraries[0];
        Assert.Null(lib.Artifact);
        Assert.NotNull(lib.NativeArtifact);
        Assert.Contains("META-INF/", lib.ExtractExcludes);
    }

    [Fact]
    public void Fabric_LibraryUrlDerivedFromMavenCoordinates()
    {
        var lib = VersionInfoParser.Parse(Fabric).Libraries[0];
        Assert.Equal("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.15.11/fabric-loader-0.15.11.jar", lib.Artifact!.Url);
        Assert.Equal("f1", lib.Artifact.Sha1);
    }

    [Fact]
    public void MavenPath_HandlesClassifierAndExtension()
    {
        Assert.Equal("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar", VersionInfoParser.MavenPath("org.lwjgl:lwjgl:3.3.1:natives-windows"));
        Assert.Equal("net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-universal.zip", VersionInfoParser.MavenPath("net.minecraftforge:forge:1.20.1-47.2.0:universal@zip"));
    }

    [Fact]
    public void Merge_ChildOverridesParent()
    {
        var parent = VersionInfoParser.Parse(Modern);
        var merged = VersionMerger.Merge(VersionInfoParser.Parse(Fabric), parent);
        Assert.Equal("fabric-loader-0.15.11-1.20.1", merged.Id);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", merged.MainClass);
        Assert.Equal("1.20.1", merged.JarId);
        Assert.Equal(17, merged.JavaMajor);
        Assert.Equal("c1", merged.ClientJar!.Sha1);
        // com.mojang:logging есть и у Fabric (1.2.0), и у родителя (1.1.1) — остаётся версия ребёнка
        Assert.Equal(1, merged.Libraries.Count(l => l.Key == "com.mojang:logging"));
        Assert.Contains("1.2.0", merged.Libraries.First(l => l.Key == "com.mojang:logging").Name);
    }
}

public class RuleTests
{
    private static Rule R(bool allow, string? os = null, string? arch = null, string? ver = null, Dictionary<string, bool>? f = null) => new(allow, os, arch, ver, f);

    [Fact] public void NoRules_Allowed() => Assert.True(RuleEvaluator.IsAllowed(new List<Rule>()));
    [Fact] public void WindowsAllow() => Assert.True(RuleEvaluator.IsAllowed(new[] { R(true, "windows") }));
    [Fact] public void OsxAllow_NotForWindows() => Assert.False(RuleEvaluator.IsAllowed(new[] { R(true, "osx") }));
    [Fact] public void AllowThenDisallowWindows() => Assert.False(RuleEvaluator.IsAllowed(new[] { R(true), R(false, "windows") }));
    [Fact] public void ArchX86_NotMatched() => Assert.False(RuleEvaluator.IsAllowed(new[] { R(true, "windows", "x86") }));
    [Fact] public void Windows10VersionRegex() => Assert.True(RuleEvaluator.IsAllowed(new[] { R(true, "windows", null, "^10\\.") }));
    [Fact] public void DemoFeatureOffByDefault() => Assert.False(RuleEvaluator.IsAllowed(new[] { R(true, null, null, null, new() { ["is_demo_user"] = true }) }));
}

public class LaunchArgumentsTests
{
    private static LaunchContext Ctx() => new()
    {
        PlayerName = "Steve", Uuid = "abc123", AccessToken = "TOKEN", VersionName = "1.20.1", GameDirectory = @"C:\g",
        AssetsRoot = @"C:\a", AssetsIndexName = "5", NativesDirectory = @"C:\n", LibraryDirectory = @"C:\l",
        Classpath = new[] { @"C:\l\a.jar", @"C:\v\1.20.1.jar" }, RamMb = 4096, LoggingConfigPath = @"C:\log.xml",
    };

    [Fact]
    public void Modern_BuildsExpectedArguments()
    {
        var args = LaunchArgumentsBuilder.Build(VersionInfoParser.Parse(VersionParsingTests.Modern), Ctx());
        Assert.Equal("-Xmx4096M", args[0]);
        Assert.Contains("-XX:HeapDumpPath=dump.heapdump", args);              // правило windows
        Assert.False(args.Contains("-XstartOnFirstThread"));                   // только macOS
        Assert.Contains(@"C:\l\a.jar;C:\v\1.20.1.jar", args);                  // classpath
        Assert.Contains(@"-Djava.library.path=C:\n".Replace(@"-Djava.library.path=", "-Djava.library.path="), args);
        Assert.Contains(@"-Dlog4j.configurationFile=C:\log.xml", args);        // защита от Log4Shell
        Assert.False(args.Contains("--demo"));
        var main = args.IndexOf("net.minecraft.client.main.Main");
        Assert.True(main > 0 && args.IndexOf("--username") > main);
        Assert.Equal("Steve", args[args.IndexOf("--username") + 1]);
    }

    [Fact]
    public void Legacy_UsesMinecraftArgumentsAndDefaultJvm()
    {
        var args = LaunchArgumentsBuilder.Build(VersionInfoParser.Parse(VersionParsingTests.Legacy), Ctx());
        Assert.Contains("-cp", args);
        Assert.Contains(@"-Djava.library.path=C:\n", args);
        Assert.Equal("TOKEN", args[args.IndexOf("--accessToken") + 1]);
        Assert.Equal(@"C:\g", args[args.IndexOf("--gameDir") + 1]);
    }

    [Fact]
    public void Fabric_JvmArgWithSpacesStaysSingleArgument()
    {
        var merged = VersionMerger.Merge(VersionInfoParser.Parse(VersionParsingTests.Fabric), VersionInfoParser.Parse(VersionParsingTests.Modern));
        var args = LaunchArgumentsBuilder.Build(merged, Ctx());
        Assert.Contains("-DFabricMcEmu= net.minecraft.client.main.Main ", args);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", args[args.IndexOf("net.fabricmc.loader.impl.launch.knot.KnotClient")]);
    }
}

public class MiscLogicTests
{
    [Fact]
    public void Pkce_MatchesRfc7636Example() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", MicrosoftAuthService.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Pkce_VerifierIsUniqueAndUrlSafe()
    {
        var a = MicrosoftAuthService.CreateVerifier();
        Assert.True(a.Length >= 43 && a != MicrosoftAuthService.CreateVerifier());
        Assert.False(a.Contains('+') || a.Contains('/') || a.Contains('='));
    }

    [Fact]
    public void JavaVersionParsing()
    {
        Assert.Equal(8, JavaService.ParseMajor("java version \"1.8.0_392\"\nJava(TM) SE Runtime"));
        Assert.Equal(17, JavaService.ParseMajor("openjdk version \"17.0.9\" 2023-10-17"));
        Assert.Equal(21, JavaService.ParseMajor("openjdk version \"21\" 2023-09-19"));
        Assert.Equal(0, JavaService.ParseMajor("что-то другое"));
    }

    [Fact]
    public void LoaderVersionFilters()
    {
        var forge = LoaderService.ParseForgeVersions(new[] { "1.19.4-45.1.0", "1.20.1-47.1.0", "1.20.1-47.2.0", "1.20.2-48.0.1" }, "1.20.1");
        Assert.Equal(new[] { "47.2.0", "47.1.0" }, forge.ToArray());
        var neo = LoaderService.ParseNeoForgeVersions(new[] { "20.4.1", "21.0.5", "21.1.1", "21.1.50" }, "1.21.1");
        Assert.Equal(new[] { "21.1.50", "21.1.1" }, neo.ToArray());
        Assert.Equal(new[] { "21.0.5" }, LoaderService.ParseNeoForgeVersions(new[] { "21.0.5", "21.1.1" }, "1.21").ToArray());
        Assert.Equal("1.2.0", LoaderService.PickDefault(new[] { "1.3.0-beta.1", "1.2.0" }));
    }

    [Fact]
    public void SkinPngValidation()
    {
        MinecraftProfileApi.ValidateSkinPng(Png(64, 64));
        MinecraftProfileApi.ValidateSkinPng(Png(64, 32));
        Assert.Throws<InvalidDataException>(() => MinecraftProfileApi.ValidateSkinPng(Png(128, 128)));
        Assert.Throws<InvalidDataException>(() => MinecraftProfileApi.ValidateSkinPng(new byte[40]));
    }

    [Fact]
    public void NickValidation()
    {
        Assert.True(MinecraftProfileApi.IsValidName("Vexlor_01"));
        Assert.False(MinecraftProfileApi.IsValidName("ab"));
        Assert.False(MinecraftProfileApi.IsValidName("имя"));
        Assert.False(MinecraftProfileApi.IsValidName("name with space"));
    }

    [Fact]
    public void ProfileParsing_UpgradesTextureUrlToHttps()
    {
        var p = MinecraftProfileApi.ParseProfile("""
        {"id":"069a79f444e94726a5befca90e38aaf5","name":"Notch",
         "skins":[{"id":"s1","state":"ACTIVE","url":"http://textures.minecraft.net/texture/abc","variant":"CLASSIC"}],
         "capes":[{"id":"c1","state":"INACTIVE","url":"http://textures.minecraft.net/texture/cape","alias":"Migrator"}]}
        """);
        Assert.Equal("Notch", p.Name);
        Assert.Equal("https://textures.minecraft.net/texture/abc", p.ActiveSkin!.Url);
        Assert.Null(p.ActiveCape);
        Assert.Equal(1, p.Capes.Count);
    }

    private static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        b[11] = 13; b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[18] = (byte)(w >> 8); b[19] = (byte)w; b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }
}
