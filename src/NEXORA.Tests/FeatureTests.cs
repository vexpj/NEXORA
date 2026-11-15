using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NEXORA.Core.Appearance;
using NEXORA.Core.Diagnostics;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;
using NEXORA.Core.Packs;
using NEXORA.Core.Perf;
using NEXORA.Core.Servers;
using NEXORA.Infrastructure.Diagnostics;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Mods;
using NEXORA.Infrastructure.Net;
using NEXORA.Infrastructure.Packs;
using NEXORA.Infrastructure.Servers;
using NEXORA.Infrastructure.Storage;
using Xunit;

namespace NEXORA.Tests;

public class CrashAnalyzerTests
{
    private static CrashContext Ctx(string log, int ram = 4096, long phys = 16384, LoaderType loader = LoaderType.Fabric, bool customJava = false, string? crash = null, string? hs = null, int exit = 1) =>
        new() { LogText = log, CrashReportText = crash, JvmCrashText = hs, CurrentRamMb = ram, PhysicalMemoryMb = phys, Loader = loader, UsesCustomJava = customJava, ExitCode = exit, MinecraftVersion = "1.20.1" };

    private static Diagnosis First(List<Diagnosis> l) => l[0];

    [Fact]
    public void Oom_SuggestsMoreMemory()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("Exception in thread \"Render thread\" java.lang.OutOfMemoryError: Java heap space", ram: 2048)));
        Assert.Equal("oom", d.Id);
        Assert.Equal("Недостаточно выделенной памяти", d.Title);
        Assert.Equal(3072, d.Fixes.First(f => f.Kind == FixKind.SetRam).IntValue);
    }

    [Fact]
    public void Oom_AlreadyAtLimit_OffersNoSetRam()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("java.lang.OutOfMemoryError: Java heap space", ram: 9728, loader: LoaderType.Vanilla)));
        Assert.Equal("oom", d.Id);
        Assert.False(d.Fixes.Any(f => f.Kind == FixKind.SetRam));
        Assert.Contains("почти максимум", d.Explanation);
    }

    [Fact]
    public void HeapReserveFailure_LowersMemory()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("Error occurred during initialization of VM\nCould not reserve enough space for 8388608KB object heap", ram: 8192, phys: 8192)));
        Assert.Equal("heap-reserve", d.Id);
        Assert.Equal(4096, d.Fixes.First().IntValue);
    }

    [Fact]
    public void WrongJava_ExplainsAndOffersAutoJava()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("java.lang.UnsupportedClassVersionError: x/Y has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0", customJava: true)));
        Assert.Equal("java-version", d.Id);
        Assert.Contains("Java 21", d.Explanation);
        Assert.Contains("Java 17", d.Explanation);
        Assert.True(d.Fixes.Any(f => f.Kind == FixKind.ClearJavaPath));
    }

    [Fact]
    public void FabricMissingAndWrongVersionDependencies()
    {
        var log = """
        net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
        A potential solution has been determined:
        	 - Install sodium, version 0.5.0 or later.
        Unmet dependency listing:
        	 - Mod 'Sodium Extra' (sodium-extra) 0.5.4 requires version 0.5.0 or later of mod 'Sodium' (sodium), but only the wrong version is present: 0.4.10+build.27!
        	 - Mod 'Iris Shaders' (iris) 1.6.0 requires any version of mod 'Fabric API' (fabric-api), which is missing!
        	 - Mod 'Old Mod' (oldmod) 1.0 requires version 1.19.2 of minecraft (minecraft), but only the wrong version is present: 1.20.1!
        """;
        var d = First(CrashAnalyzer.Analyze(Ctx(log)));
        Assert.Equal("mods-conflict", d.Id);
        Assert.Contains("«Sodium Extra» нужен «Sodium» версии 0.5.0 or later, а установлена 0.4.10+build.27", d.Explanation);
        Assert.Contains("«Iris Shaders» нужен мод «Fabric API», но его нет", d.Explanation);
        Assert.Contains("не подходит к вашей версии Minecraft (1.20.1)", d.Explanation);
        Assert.True(d.Fixes.Any(f => f.Kind == FixKind.DisableMod && f.Text == "sodium-extra"));
        Assert.True(d.Fixes.Count(f => f.Kind == FixKind.DisableMod) <= 3);
    }

    [Fact]
    public void ForgeMandatoryDependencies()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("Missing or unsupported mandatory dependencies:\n\tMod ID: 'create', Requested by: 'createaddition', Expected range: '[0.5.1,)', Actual version: '[MISSING]'", loader: LoaderType.Forge)));
        Assert.Equal("mods-conflict", d.Id);
        Assert.Contains("«createaddition» нужен «create»", d.Explanation);
        Assert.True(d.Fixes.Any(f => f.Kind == FixKind.DisableMod && f.Text == "createaddition"));
    }

    [Fact]
    public void MixinFailure_BlamesModFromConfigName()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: An unexpected critical error was encountered\nCaused by: InvalidMixinException: @Inject annotation on onTick in sodium.mixins.json:core.MixinFoo failed")));
        Assert.Equal("mixin", d.Id);
        Assert.True(d.Fixes.Any(f => f.Kind == FixKind.DisableMod && f.Text == "sodium"));
    }

    [Fact]
    public void CrashReport_SuspectedMods_Fabric_And_Forge_Styles()
    {
        var fabric = "---- Minecraft Crash Report ----\nDescription: Ticking entity\n-- System Details --\nSuspected Mods: \n\tLithium (lithium), Version: 0.11.2\n\tMinecraft (minecraft), Version: 1.20.1\n\nStacktrace:";
        var d = First(CrashAnalyzer.Analyze(Ctx("", crash: fabric)));
        Assert.Equal("suspect-mod", d.Id);
        Assert.True(d.Fixes.Any(f => f.Text == "lithium"));
        Assert.False(d.Fixes.Any(f => f.Text == "minecraft"));

        var forge = "Description: x\nSuspected Mods: Create (create), Version: 0.5.1\nStacktrace:";
        Assert.Equal("create", CrashAnalyzer.Analyze(Ctx("", crash: forge))[0].Fixes.First(f => f.Kind == FixKind.DisableMod).Text);
        Assert.False(CrashAnalyzer.Analyze(Ctx("", crash: "Suspected Mods: None\n")).Any(d => d.Id == "suspect-mod"));
    }

    [Fact]
    public void GpuDriverCrash_FromHsErr()
    {
        var hs = "# A fatal error has been detected by the Java Runtime Environment:\n#\n#  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x00007ffd1a2b3c4d, pid=1234, tid=5678\n#\n# Problematic frame:\n# C  [nvoglv64.dll+0x1a2b3c]\n";
        var d = First(CrashAnalyzer.Analyze(Ctx("", hs: hs)));
        Assert.Equal("gpu-driver", d.Id);
        Assert.Contains("NVIDIA", d.Title);
        Assert.Equal("https://www.nvidia.com/Download/index.aspx", d.Fixes.First().Text);
    }

    [Fact]
    public void OverlayCrash_Detected()
    {
        var hs = "# Problematic frame:\n# C  [DiscordHook64.dll+0x1234]\n# EXCEPTION_ACCESS_VIOLATION";
        Assert.Equal("overlay", First(CrashAnalyzer.Analyze(Ctx("", hs: hs))).Id);
    }

    [Fact] public void OpenGlError() => Assert.Equal("opengl", First(CrashAnalyzer.Analyze(Ctx("GLFW error 65542: WGL: The driver does not appear to support OpenGL"))).Id);
    [Fact] public void MissingMainClass() => Assert.Equal("missing-files", First(CrashAnalyzer.Analyze(Ctx("Error: Could not find or load main class net.minecraft.client.main.Main", loader: LoaderType.Vanilla))).Id);
    [Fact] public void ExitCodeOnly() => Assert.Equal("native-exit", First(CrashAnalyzer.Analyze(Ctx("", exit: -1073740791))).Id);

    [Fact]
    public void Unknown_FallsBackToDescription()
    {
        var d = First(CrashAnalyzer.Analyze(Ctx("", crash: "---- Minecraft Crash Report ----\nDescription: Rendering screen\n", exit: 1)));
        Assert.Equal("unknown", d.Id);
        Assert.Contains("Rendering screen", d.Explanation);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void Errors_SortedBeforeWarnings()
    {
        var l = CrashAnalyzer.Analyze(Ctx("401 Unauthorized\njava.lang.OutOfMemoryError: Java heap space"));
        Assert.Equal("oom", l[0].Id);
        Assert.Equal("session", l[1].Id);
    }

    [Fact]
    public void HugeLogDoesNotHang()
    {
        var big = new string('a', 3_000_000) + "\nMod '" + new string('x', 50000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        CrashAnalyzer.Analyze(Ctx(big));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15));
    }
}

public class ServerTests
{
    [Fact]
    public void AddressParsing()
    {
        Assert.True(ServerAddress.TryParse("mc.example.com", out var a) && a.Host == "mc.example.com" && a.Port == 25565);
        Assert.True(ServerAddress.TryParse(" mc.example.com:25570 ", out a) && a.Port == 25570);
        Assert.True(ServerAddress.TryParse("1.2.3.4:1", out a) && a.Port == 1);
        Assert.True(ServerAddress.TryParse("[::1]:25566", out a) && a.Host == "::1" && a.Port == 25566);
        Assert.Equal("[::1]:25566", a.ToString());
        foreach (var bad in new[] { "", "bad host", "host:99999", "host:0", "a:b:c", "evil.com/path", "user@host", "-host", "host.", "[::1", "[abc]:25565" })
            Assert.False(ServerAddress.TryParse(bad, out _));
    }

    [Fact]
    public async Task VarIntRoundTrip()
    {
        foreach (var v in new[] { 0, 1, 127, 128, 300, 25565, int.MaxValue, -1 })
        {
            var ms = new MemoryStream();
            McProtocol.WriteVarInt(ms, v);
            ms.Position = 0;
            Assert.Equal(v, await McProtocol.ReadVarIntAsync(ms, CancellationToken.None));
        }
        var neg = new MemoryStream(); McProtocol.WriteVarInt(neg, -1);
        Assert.Equal(5, (int)neg.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => McProtocol.ReadVarIntAsync(new MemoryStream(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }), CancellationToken.None));
    }

    [Fact]
    public void StatusJsonParsing()
    {
        var s = McProtocol.ParseStatusJson("""{"version":{"name":"Paper 1.20.1","protocol":763},"players":{"max":100,"online":42},"description":{"text":"","extra":[{"text":"§aДобро пожаловать "},{"text":"на сервер"}]}}""", 33);
        Assert.True(s.Online);
        Assert.Equal("Добро пожаловать на сервер", s.Motd);
        Assert.Equal(42, s.Players); Assert.Equal(100, s.MaxPlayers); Assert.Equal("Paper 1.20.1", s.Version); Assert.Equal(33, s.LatencyMs);
        Assert.Equal("Hello", McProtocol.ParseStatusJson("""{"description":"§l§6Hello","players":{"online":0,"max":1},"version":{"name":"x"}}""", 1).Motd);
    }

    private static async Task<(TcpListener l, Task t)> Serve(Func<NetworkStream, Task> handler)
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var t = Task.Run(async () => { using var c = await l.AcceptTcpClientAsync(); using var s = c.GetStream(); await handler(s); });
        await Task.Yield();
        return (l, t);
    }

    [Fact]
    public async Task Pinger_ReadsRealStatusOverTcp()
    {
        const string json = """{"version":{"name":"1.20.1","protocol":763},"players":{"max":20,"online":7},"description":"Тестовый сервер"}""";
        var (l, t) = await Serve(async s =>
        {
            await McProtocol.ReadVarIntAsync(s, CancellationToken.None);           // длина handshake
            var hs = new byte[(int)0]; var len = new byte[1];
            // читаем handshake и status request: просто дочитываем доступные байты
            await Task.Delay(100);
            var body = new MemoryStream();
            var jb = Encoding.UTF8.GetBytes(json);
            var inner = new MemoryStream();
            McProtocol.WriteVarInt(inner, 0); McProtocol.WriteVarInt(inner, jb.Length); inner.Write(jb);
            McProtocol.WriteVarInt(body, (int)inner.Length); body.Write(inner.ToArray());
            await s.WriteAsync(body.ToArray());
            await s.FlushAsync();
            await Task.Delay(200);
        });
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var st = await new ServerPinger().PingAsync(new ServerEndpoint("127.0.0.1", port), CancellationToken.None);
        l.Stop();
        Assert.True(st.Online);
        Assert.Equal(7, st.Players);
        Assert.Equal("Тестовый сервер", st.Motd);
        Assert.True(st.LatencyMs >= 0);
    }

    [Fact]
    public async Task Pinger_HandlesGarbageAndClosedPort()
    {
        var (l, _) = await Serve(async s => { await s.WriteAsync(new byte[] { 0x05, 0x01, 0x02, 0x03, 0x04, 0x05 }); await Task.Delay(300); });
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var bad = await new ServerPinger().PingAsync(new ServerEndpoint("127.0.0.1", port), CancellationToken.None);
        l.Stop();
        Assert.False(bad.Online);

        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var p2 = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        var closed = await new ServerPinger().PingAsync(new ServerEndpoint("127.0.0.1", p2), CancellationToken.None);
        Assert.False(closed.Online);
        Assert.Equal("недоступен", closed.Error);
    }

    [Fact]
    public void QuickConnect_ModernUsesQuickPlay_LegacyUsesServerPort()
    {
        const string modern = """
        {"id":"1.20.1","mainClass":"M","arguments":{"game":["--username","${auth_player_name}",
          {"rules":[{"action":"allow","features":{"is_quick_play_multiplayer":true}}],"value":["--quickPlayMultiplayer","${quickPlayMultiplayer}"]},
          {"rules":[{"action":"allow","features":{"has_quick_plays_support":true}}],"value":["--quickPlayPath","${quickPlayPath}"]}],"jvm":["-cp","${classpath}"]}}
        """;
        var ctx = new LaunchContext { PlayerName = "S", Server = new ServerEndpoint("play.example.com", 25565) };
        var args = LaunchArgumentsBuilder.Build(VersionInfoParser.Parse(modern), ctx);
        Assert.Equal("play.example.com:25565", args[args.IndexOf("--quickPlayMultiplayer") + 1]);
        Assert.False(args.Contains("--quickPlayPath"));
        Assert.False(args.Contains("--server"));
        ctx.Server = null;
        Assert.False(LaunchArgumentsBuilder.Build(VersionInfoParser.Parse(modern), ctx).Contains("--quickPlayMultiplayer"));

        ctx.Server = new ServerEndpoint("old.example.com", 25570);
        var legacy = LaunchArgumentsBuilder.Build(VersionInfoParser.Parse(VersionParsingTests.Legacy), ctx);
        Assert.Equal("old.example.com", legacy[legacy.IndexOf("--server") + 1]);
        Assert.Equal("25570", legacy[legacy.IndexOf("--port") + 1]);
    }
}

public class PerfAndAppearanceTests
{
    [Fact]
    public void CpuPercentMath()
    {
        Assert.Equal(12.5, PerfMath.CpuPercent(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), 4));
        Assert.Equal(100.0, PerfMath.CpuPercent(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), 2));
        Assert.Equal(0.0, PerfMath.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4));
    }

    [Fact]
    public void NvidiaSmiParsing()
    {
        var g = PerfMath.ParseNvidiaSmi("NVIDIA GeForce RTX 3060, 12, 54, 1234, 12288\n");
        Assert.Equal("NVIDIA GeForce RTX 3060", g!.Name);
        Assert.Equal(12, g.UtilPercent); Assert.Equal(54, g.TempC); Assert.Equal(1234, g.MemUsedMb); Assert.Equal(12288, g.MemTotalMb);
        Assert.Null(PerfMath.ParseNvidiaSmi("NVIDIA X, [N/A], [N/A], 1, 2"));
        Assert.Null(PerfMath.ParseNvidiaSmi(""));
        Assert.Null(PerfMath.ParseNvidiaSmi(null));
    }

    [Fact]
    public void MemoryLimitWarning()
    {
        Assert.True(PerfMath.NearMemoryLimit(3900, 4096));
        Assert.False(PerfMath.NearMemoryLimit(2000, 4096));
    }

    [Fact]
    public void Appearance_NormalizesAndPresetsAreValid()
    {
        var a = new AppearanceSettings { ThemeName = "нет такой", AccentHex = "#abc", BackgroundOpacity = 5, BackgroundImagePath = "  " }.Normalize();
        Assert.Equal(ThemePresets.Default.Name, a.ThemeName);
        Assert.Null(a.AccentHex); Assert.Equal(1.0, a.BackgroundOpacity); Assert.Null(a.BackgroundImagePath);
        Assert.Equal("#FF8800", new AppearanceSettings { AccentHex = "#ff8800" }.Normalize().AccentHex);
        Assert.Equal(0.25, new AppearanceSettings { BackgroundOpacity = double.NaN }.Normalize().BackgroundOpacity);
        foreach (var p in ThemePresets.All)
            foreach (var c in new[] { p.Background, p.Surface, p.SecondarySurface, p.Accent, p.PrimaryText, p.SecondaryText })
                Assert.True(HexColor.IsValid(c));
    }

    [Fact]
    public void Appearance_SurvivesSettingsRoundTrip_AndOldFilesLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"), "s.json");
        var store = new JsonSettingsStore(path);
        var s = new LauncherSettings();
        s.Appearance.ThemeName = "Океан"; s.Appearance.SidebarRight = true; s.Appearance.AnimationsEnabled = false;
        store.Save(s);
        var l = store.Load();
        Assert.Equal("Океан", l.Appearance.ThemeName); Assert.True(l.Appearance.SidebarRight); Assert.False(l.Appearance.AnimationsEnabled);

        File.WriteAllText(path, "{\"RamMb\":6144}");   // файл от версии без оформления
        var old = store.Load();
        Assert.Equal(6144, old.RamMb);
        Assert.Equal(ThemePresets.Default.Name, old.Appearance.ThemeName);
    }
}

public class PackTests
{
    private static (LauncherPaths paths, ModProvenanceStore prov, GameProfile profile) Env()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(root, Path.Combine(root, "data"));
        return (paths, new ModProvenanceStore(paths), new GameProfile { Name = "Моя сборка", MinecraftVersion = "1.20.1", Loader = LoaderType.Fabric, LoaderVersion = "0.15.11" });
    }

    private static PackMod Mod(string file = "sodium.jar", string url = "https://cdn.modrinth.com/data/x/sodium.jar", string? sha1 = null) =>
        new() { Source = "Modrinth", ProjectId = "AANobbMI", FileName = file, Url = url, Sha1 = sha1 ?? new string('a', 40), Version = "0.5.8" };

    private static PackManifest Manifest(params PackMod[] mods) =>
        new() { Name = "Test", MinecraftVersion = "1.20.1", Loader = LoaderType.Fabric, LoaderVersion = "0.15.11", Mods = mods.ToList() };

    private static void Reject(PackManifest m) => Assert.Throws<InvalidDataException>(() => PackValidator.Validate(m, PackService.IsUrlAllowed));

    [Fact]
    public void Validator_AcceptsGoodAndRejectsHostile()
    {
        PackValidator.Validate(Manifest(Mod()), PackService.IsUrlAllowed);
        Reject(Manifest(Mod(url: "https://evil.example.com/a.jar")));
        Reject(Manifest(Mod(url: "http://cdn.modrinth.com/a.jar")));
        Reject(Manifest(Mod(file: "../../evil.jar")));
        Reject(Manifest(Mod(file: "run.exe")));
        Reject(Manifest(new PackMod { Source = "Modrinth", FileName = "a.jar", Url = "https://cdn.modrinth.com/a.jar" }));   // без хэша
        Reject(Manifest(new PackMod { Source = "EvilHub", FileName = "a.jar", Url = "https://cdn.modrinth.com/a.jar", Sha1 = new string('a', 40) }));
        var m = Manifest(Mod()); m.Name = ""; Reject(m);
        m = Manifest(Mod()); m.MinecraftVersion = "1.20; rm -rf"; Reject(m);
        m = Manifest(Mod()); m.FormatVersion = 99; Reject(m);
        Reject(Manifest(Enumerable.Range(0, 501).Select(i => Mod($"m{i}.jar")).ToArray()));
    }

    [Fact]
    public void Code_RoundTripAndGarbage()
    {
        var code = PackCodec.Encode(Manifest(Mod(), Mod("lithium.jar", sha1: new string('b', 40))));
        Assert.True(code.StartsWith("NEXORA1:"));
        var m = PackCodec.Decode(code);
        Assert.Equal(2, m.Mods.Count); Assert.Equal(LoaderType.Fabric, m.Loader); Assert.Equal("Test", m.Name);
        Assert.Throws<InvalidDataException>(() => PackCodec.Decode("hello"));
        Assert.Throws<InvalidDataException>(() => PackCodec.Decode("NEXORA1:@@@@"));
        Assert.Throws<InvalidDataException>(() => PackCodec.Decode("NEXORA1:AAAA"));
    }

    [Fact]
    public void Code_ZipBombIsRejected()
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) ds.Write(new byte[8 * 1024 * 1024]);
        var code = "NEXORA1:" + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ex = Assert.Throws<InvalidDataException>(() => PackCodec.Decode(code));
        Assert.Contains("слишком большой", ex.Message);
    }

    [Fact]
    public void Export_UsesProvenance_SkipsManualAndSecretConfigs()
    {
        var (paths, prov, p) = Env();
        var mods = paths.ModsDir(p.Id); Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "sodium.jar"), "x"); File.WriteAllText(Path.Combine(mods, "manual.jar"), "x"); File.WriteAllText(Path.Combine(mods, "off.jar.disabled"), "x");
        prov.Upsert(p.Id, new ModRecord { FileName = "sodium.jar", Source = "Modrinth", ProjectId = "AANobbMI", Url = "https://cdn.modrinth.com/data/x/sodium.jar", Sha1 = new string('c', 40), Version = "0.5.8" });
        var cfg = Path.Combine(paths.InstanceDir(p.Id), "config"); Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "sodium.json"), "{}"); File.WriteAllText(Path.Combine(cfg, "discord-token.json"), "SECRET");
        File.WriteAllBytes(Path.Combine(cfg, "huge.bin"), new byte[600 * 1024]);

        var svc = new PackService(paths, prov, new FileDownloader(new SafeHttp(new HttpClient(new FakeHandler(_ => new HttpResponseMessage())))));
        var dest = Path.Combine(paths.Root, "out", "pack.nexora-pack");
        var res = svc.ExportFile(p, dest, includeConfigs: true);
        Assert.Equal(1, res.Exported);
        Assert.Equal(new[] { "manual.jar" }, res.SkippedManual.ToArray());
        Assert.Equal(1, res.ConfigFiles);
        using (var zip = ZipFile.OpenRead(dest))
        {
            Assert.NotNull(zip.GetEntry("pack.json"));
            Assert.NotNull(zip.GetEntry("config/sodium.json"));
            Assert.Null(zip.GetEntry("config/discord-token.json"));
            Assert.Null(zip.GetEntry("config/huge.bin"));
        }
        var back = svc.ReadFile(dest);
        Assert.Equal("Моя сборка", back.Name); Assert.True(back.HasConfigs); Assert.Equal(1, back.Mods.Count);
        Assert.Equal(1, PackCodec.Decode(svc.ExportCode(p, out _)).Mods.Count);
    }

    [Fact]
    public async Task Import_DownloadsVerifiesAndRecords()
    {
        var (paths, prov, _) = Env();
        var data = Encoding.UTF8.GetBytes("jar-bytes");
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        var svc = new PackService(paths, prov, new FileDownloader(new SafeHttp(new HttpClient(h))));
        var m = Manifest(Mod(sha1: Hashing.Sha1Hex(data)));
        var p = await svc.ImportAsync(m, null, null, CancellationToken.None);
        Assert.Equal("Test", p.Name); Assert.Equal(LoaderType.Fabric, p.Loader);
        Assert.Equal("jar-bytes", File.ReadAllText(Path.Combine(paths.ModsDir(p.Id), "sodium.jar")));
        Assert.Equal("sodium.jar", prov.Load(p.Id).Single().FileName);
    }

    [Fact]
    public async Task Import_TamperedMod_FailsAndCleansUp()
    {
        var (paths, prov, _) = Env();
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9, 9, 9 }) });
        var svc = new PackService(paths, prov, new FileDownloader(new SafeHttp(new HttpClient(h))));
        await Assert.ThrowsAsync<IntegrityException>(() => svc.ImportAsync(Manifest(Mod()), null, null, CancellationToken.None));
        Assert.False(Directory.Exists(paths.Instances) && Directory.GetDirectories(paths.Instances).Length > 0);
    }

    [Fact]
    public async Task Import_ConfigPathTraversalInArchive_IsBlocked()
    {
        var (paths, prov, _) = Env();
        Directory.CreateDirectory(paths.Root);
        var zipPath = Path.Combine(paths.Root, "evil.nexora-pack");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("pack.json").Open())) w.Write("{}");
            using var w2 = new StreamWriter(zip.CreateEntry("config/../../evil.txt").Open()); w2.Write("pwned");
        }
        var svc = new PackService(paths, prov, new FileDownloader(new SafeHttp(new HttpClient(new FakeHandler(_ => new HttpResponseMessage())))));
        var m = Manifest(); m.HasConfigs = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.ImportAsync(m, zipPath, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(paths.Root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(paths.Instances, "evil.txt")));
    }

    [Fact]
    public async Task ModInstaller_RecordsProvenanceAndDeleteRemovesIt()
    {
        var (paths, prov, p) = Env();
        var data = Encoding.UTF8.GetBytes("abc");
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        var inst = new ModInstaller(paths, new FileDownloader(new SafeHttp(new HttpClient(h))), prov);
        var file = new ModFile { Source = "Modrinth", ProjectId = "P1", FileName = "x.jar", DownloadUrl = "https://cdn.modrinth.com/x.jar", Sha1 = Hashing.Sha1Hex(data), Version = "1.0" };
        await inst.InstallAsync(p, null!, file, CancellationToken.None);
        Assert.Equal("P1", prov.Load(p.Id).Single().ProjectId);
        inst.Delete(p, "x.jar");
        Assert.Equal(0, prov.Load(p.Id).Count);
    }
}

public class FixServiceTests
{
    private static (LauncherPaths paths, FixService fix, LauncherSettings settings, GameProfile p, ModInstaller mods, string settingsPath) Env()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(root, Path.Combine(root, "data"));
        var sp = Path.Combine(root, "s.json");
        var store = new JsonSettingsStore(sp);
        var settings = new LauncherSettings { JavaPath = @"C:\j\javaw.exe" };
        var mods = new ModInstaller(paths, new FileDownloader(new SafeHttp(new HttpClient(new FakeHandler(_ => new HttpResponseMessage())))));
        return (paths, new FixService(paths, store, settings, mods), settings, new GameProfile { Name = "t", MinecraftVersion = "1.20.1", Loader = LoaderType.Fabric }, mods, sp);
    }

    [Fact]
    public void SetRam_And_ClearJava_PersistToSettings()
    {
        var (_, fix, settings, p, _, sp) = Env();
        fix.Apply(new FixAction(FixKind.SetRam, "", 6144), p);
        fix.Apply(new FixAction(FixKind.ClearJavaPath, ""), p);
        Assert.Equal(6144, settings.RamMb); Assert.Null(settings.JavaPath);
        var saved = new JsonSettingsStore(sp).Load();
        Assert.Equal(6144, saved.RamMb); Assert.Null(saved.JavaPath);
        p.RamMb = 2048;   // у профиля свой лимит — меняем его
        Assert.True(fix.Apply(new FixAction(FixKind.SetRam, "", 8192), p).ProfileChanged);
        Assert.Equal(8192, p.RamMb);
    }

    [Fact]
    public void DisableMod_FindsByIdAndIsReversible()
    {
        var (paths, fix, _, p, mods, _) = Env();
        var dir = paths.ModsDir(p.Id); Directory.CreateDirectory(dir);
        using (var zip = ZipFile.Open(Path.Combine(dir, "sodium-extra-0.5.4.jar"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) w.Write("""{"id":"sodium-extra","name":"Sodium Extra","version":"0.5.4"}""");
        var r = fix.Apply(new FixAction(FixKind.DisableMod, "", 0, "sodium-extra"), p);
        Assert.Contains("sodium-extra-0.5.4.jar", r.Message);
        Assert.True(File.Exists(Path.Combine(dir, "sodium-extra-0.5.4.jar.disabled")));
        mods.SetEnabled(p, "sodium-extra-0.5.4.jar.disabled", true);
        Assert.True(File.Exists(Path.Combine(dir, "sodium-extra-0.5.4.jar")));
        Assert.Contains("Не удалось найти", fix.Apply(new FixAction(FixKind.DisableMod, "", 0, "nothing-like-this"), p).Message);
    }

    [Fact]
    public void Repair_RemovesOnlyCachesAndKeepsWorlds()
    {
        var (paths, fix, _, p, _, _) = Env();
        p.InstalledVersionId = "fabric-loader-0.15.11-1.20.1";
        Directory.CreateDirectory(Path.Combine(paths.VersionDir(p.InstalledVersionId), "natives"));
        File.WriteAllText(paths.VersionJson(p.InstalledVersionId), "{}");
        var world = Path.Combine(paths.InstanceDir(p.Id), "saves", "MyWorld", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(world)!); File.WriteAllText(world, "precious");

        var r = fix.Apply(new FixAction(FixKind.RepairInstall, ""), p);
        Assert.True(r.ProfileChanged); Assert.Null(p.InstalledVersionId);
        Assert.False(Directory.Exists(paths.VersionDir("fabric-loader-0.15.11-1.20.1")));
        Assert.Equal("precious", File.ReadAllText(world));
    }

    [Fact]
    public void OpenUrl_OnlyVendorHttpsPages()
    {
        var (_, fix, _, p, _, _) = Env();
        Assert.Contains("не разрешена", fix.Apply(new FixAction(FixKind.OpenUrl, "", 0, "https://evil.example.com/x"), p).Message);
        Assert.Contains("не разрешена", fix.Apply(new FixAction(FixKind.OpenUrl, "", 0, "http://www.nvidia.com/x"), p).Message);
        Assert.Contains("не разрешена", fix.Apply(new FixAction(FixKind.OpenUrl, "", 0, "file:///C:/Windows/System32/calc.exe"), p).Message);
    }

    [Fact]
    public void DiagnosticService_ReadsLogAndNewestCrashReport()
    {
        var (paths, _, settings, p, _, _) = Env();
        Directory.CreateDirectory(paths.LogsDir);
        var log = Path.Combine(paths.LogsDir, "latest.log");
        File.WriteAllText(log, "[main] starting\njava.lang.OutOfMemoryError: Java heap space\n");
        var crashDir = Path.Combine(paths.InstanceDir(p.Id), "crash-reports"); Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "crash-2026-11-13_12.00.00-client.txt"), "Description: Ticking entity\nSuspected Mods: Lithium (lithium), Version: 1\nStacktrace:");
        var svc = new CrashDiagnosticService(paths);
        var list = svc.Analyze(p, 1, DateTime.UtcNow.AddMinutes(-1), log, settings);
        Assert.True(list.Any(d => d.Id == "oom"));
        Assert.True(list.Any(d => d.Id == "suspect-mod"));
        // старый crash-report (до запуска) не учитывается
        var old = svc.Analyze(p, 1, DateTime.UtcNow.AddHours(1), log, settings);
        Assert.False(old.Any(d => d.Id == "suspect-mod"));
    }
}
