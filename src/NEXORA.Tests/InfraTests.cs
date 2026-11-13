using System.IO.Compression;
using System.Net;
using System.Text;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Mods;
using NEXORA.Infrastructure.Net;
using NEXORA.Infrastructure.News;
using NEXORA.Infrastructure.Storage;
using Xunit;

namespace NEXORA.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _f;
    public int Calls;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) => _f = f;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Calls++; return Task.FromResult(_f(r)); }
}

internal sealed class ReverseProtector : ISecretProtector
{
    public string Protect(string s) => "P:" + new string(s.Reverse().ToArray());
    public string Unprotect(string s) => new string(s[2..].Reverse().ToArray());
}

public class SecurityTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void HostPolicy_BlocksHttpAndUnknownHosts()
    {
        HostPolicy.Require("https://cdn.modrinth.com/data/x/versions/1/mod.jar");
        Assert.Throws<UntrustedHostException>(() => HostPolicy.Require("http://cdn.modrinth.com/x.jar"));
        Assert.Throws<UntrustedHostException>(() => HostPolicy.Require("https://evil.example.com/x.jar"));
        Assert.Throws<UntrustedHostException>(() => HostPolicy.Require("https://cdn.modrinth.com.evil.com/x.jar"));
    }

    [Fact]
    public async Task Downloader_VerifiesSha1()
    {
        var data = Encoding.UTF8.GetBytes("hello mod");
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        var dl = new FileDownloader(new SafeHttp(new HttpClient(h)));
        var dest = Path.Combine(Tmp(), "a.jar");
        await dl.DownloadAsync("https://cdn.modrinth.com/a.jar", dest, new FileHash("sha1", Hashing.Sha1Hex(data)), CancellationToken.None);
        Assert.Equal("hello mod", File.ReadAllText(dest));
        // повторный вызов при совпавшей сумме не ходит в сеть
        await dl.DownloadAsync("https://cdn.modrinth.com/a.jar", dest, new FileHash("sha1", Hashing.Sha1Hex(data)), CancellationToken.None);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Downloader_RejectsTamperedFileAndLeavesNothing()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        var dl = new FileDownloader(new SafeHttp(new HttpClient(h)));
        var dest = Path.Combine(Tmp(), "evil.jar");
        await Assert.ThrowsAsync<IntegrityException>(() => dl.DownloadAsync("https://cdn.modrinth.com/a.jar", dest, new FileHash("sha1", new string('0', 40)), CancellationToken.None, retries: 1));
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Http_BlocksRedirectToUntrustedHost()
    {
        var h = new FakeHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://evil.example.com/x.jar");
            return r;
        });
        var http = new SafeHttp(new HttpClient(h));
        await Assert.ThrowsAsync<UntrustedHostException>(() => http.GetAsync("https://cdn.modrinth.com/a.jar", CancellationToken.None));
    }

    [Fact]
    public void SafeCombine_BlocksPathTraversal()
    {
        var root = Tmp();
        Assert.Throws<InvalidDataException>(() => GameInstaller.SafeCombine(root, "../outside.txt"));
        Assert.Throws<InvalidDataException>(() => GameInstaller.SafeCombine(root, "a/../../outside.txt"));
        Assert.True(GameInstaller.SafeCombine(root, "a/b.txt").StartsWith(Path.GetFullPath(root)));
    }

    [Fact]
    public void ModFileName_Sanitized()
    {
        Assert.Equal("evil.jar", ModInstaller.SafeFileName("../../evil.jar"));
        Assert.Throws<InvalidDataException>(() => ModInstaller.SafeFileName("setup.exe"));
    }
}

public class StoreTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Settings_ClampAndRoundTrip()
    {
        Assert.Equal(LauncherSettings.MinRamMb, new LauncherSettings { RamMb = 10 }.Normalize().RamMb);
        var store = new JsonSettingsStore(Path.Combine(Tmp(), "s.json"));
        store.Save(new LauncherSettings { RamMb = 6144, GameDirectory = @"C:\mc", JavaPath = @"C:\j\javaw.exe", MicrosoftClientId = "id-1" });
        var s = store.Load();
        Assert.Equal(6144, s.RamMb);
        Assert.Equal("id-1", s.MicrosoftClientId);
    }

    [Fact]
    public void Settings_ApiKeyIsNeverWrittenInPlainText()
    {
        var path = Path.Combine(Tmp(), "s.json");
        var store = new JsonSettingsStore(path, new ReverseProtector());
        store.Save(new LauncherSettings { CurseForgeApiKey = "SECRET-KEY" });
        Assert.False(File.ReadAllText(path).Contains("SECRET-KEY"));
        Assert.Equal("SECRET-KEY", store.Load().CurseForgeApiKey);

        var noProtector = Path.Combine(Tmp(), "s2.json");
        new JsonSettingsStore(noProtector).Save(new LauncherSettings { CurseForgeApiKey = "SECRET-KEY" });
        Assert.False(File.ReadAllText(noProtector).Contains("SECRET-KEY"));
    }

    [Fact]
    public void Settings_CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(Tmp(), "s.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(LauncherSettings.DefaultRamMb, new JsonSettingsStore(path).Load().RamMb);
    }

    [Fact]
    public void Profiles_RoundTripKeepsLoaderAsText()
    {
        var path = Path.Combine(Tmp(), "p.json");
        var store = new JsonProfileStore(path);
        store.Save(new[] { new GameProfile { Name = "Моя сборка", MinecraftVersion = "1.20.1", Loader = LoaderType.Fabric, LoaderVersion = "0.15.11" } });
        Assert.True(File.ReadAllText(path).Contains("\"Fabric\""));
        var p = store.Load().Single();
        Assert.Equal(LoaderType.Fabric, p.Loader);
        Assert.Equal("1.20.1", p.LaunchVersionId);
        p.InstalledVersionId = "fabric-loader-0.15.11-1.20.1";
        Assert.Equal("fabric-loader-0.15.11-1.20.1", p.LaunchVersionId);
    }

    [Fact]
    public void AccountToken_StoredOnlyProtected()
    {
        var path = Path.Combine(Tmp(), "acc.json");
        var store = new JsonAccountStore(path, new ReverseProtector());
        store.Save(new StoredAccount("refresh-123"));
        Assert.False(File.ReadAllText(path).Contains("refresh-123"));
        Assert.Equal("refresh-123", store.Load()!.RefreshToken);
        store.Clear();
        Assert.Null(store.Load());
    }
}

public class CatalogTests
{
    [Fact]
    public void Modrinth_SearchParsing()
    {
        var r = ModrinthCatalog.ParseSearch("""{"hits":[{"project_id":"AANobbMI","slug":"sodium","title":"Sodium","description":"Fast renderer","author":"jellysquid3","downloads":1500000,"icon_url":"https://cdn.modrinth.com/i.png"}],"total_hits":1}""");
        Assert.Equal("Sodium", r[0].Title);
        Assert.Equal("https://modrinth.com/mod/sodium", r[0].PageUrl);
        Assert.Equal("1,5 млн".Replace(',', '.').Replace(" млн", " млн"), r[0].DownloadsText.Replace(',', '.'));
    }

    [Fact]
    public void Modrinth_BestFile_PrefersReleaseAndPrimary_AndRequiredDeps()
    {
        var f = ModrinthCatalog.ParseBestFile("""
        [{"version_number":"2.0-beta","version_type":"beta","files":[{"filename":"b.jar","url":"https://cdn.modrinth.com/b.jar","primary":true,"hashes":{"sha1":"1","sha512":"2"},"size":1}],"dependencies":[]},
         {"version_number":"1.9","version_type":"release","files":[{"filename":"x-sources.jar","url":"https://cdn.modrinth.com/s.jar","primary":false,"hashes":{}},{"filename":"x.jar","url":"https://cdn.modrinth.com/x.jar","primary":true,"hashes":{"sha1":"aa","sha512":"bb"},"size":9}],
          "dependencies":[{"project_id":"P7dR8mSH","dependency_type":"required"},{"project_id":"opt","dependency_type":"optional"}]}]
        """, "proj");
        Assert.Equal("x.jar", f!.FileName);
        Assert.Equal("bb", f.Sha512);
        Assert.Equal(new[] { "P7dR8mSH" }, f.RequiredDependencies.ToArray());
    }

    [Fact]
    public void CurseForge_BestFile_NullDownloadUrlIsKept()
    {
        var f = CurseForgeCatalog.ParseBestFile("""
        {"data":[{"id":1,"fileName":"old.jar","fileDate":"2023-01-01T00:00:00Z","releaseType":1,"downloadUrl":"https://edge.forgecdn.net/old.jar","hashes":[{"value":"h0","algo":1}],"dependencies":[]},
                 {"id":2,"fileName":"new.jar","fileDate":"2024-01-01T00:00:00Z","releaseType":1,"downloadUrl":null,"hashes":[{"value":"m5","algo":2},{"value":"h1","algo":1}],"dependencies":[{"modId":306612,"relationType":3},{"modId":5,"relationType":2}]}]}
        """, "42");
        Assert.Equal("new.jar", f!.FileName);
        Assert.Null(f.DownloadUrl);
        Assert.Equal("h1", f.Sha1);
        Assert.Equal(new[] { "306612" }, f.RequiredDependencies.ToArray());
    }

    [Fact]
    public void CurseForge_SearchParsing()
    {
        var r = CurseForgeCatalog.ParseSearch("""{"data":[{"id":238222,"name":"JEI","summary":"Items","downloadCount":2.5e8,"authors":[{"name":"mezz"}],"logo":{"thumbnailUrl":"https://media.forgecdn.net/x.png"},"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/jei"}}]}""");
        Assert.Equal("238222", r[0].Id);
        Assert.Equal("mezz", r[0].Author);
    }

    [Fact]
    public void News_ParsingToleratesMissingFields()
    {
        var n = NewsService.Parse("""{"version":1,"entries":[{"title":"Minecraft 1.21","type":"release","version":"1.21","date":"2024-06-13T10:00:00Z","shortText":"<p>Новые <b>крафты</b></p>","image":{"url":"/images/a.jpg"}},{"version":"24w01a"}]}""");
        Assert.Equal(2, n.Count);
        Assert.Equal("Новые крафты", n[0].Text);
        Assert.Equal("2024-06-13", n[0].Date);
        Assert.Equal("https://launchercontent.mojang.com/images/a.jpg", n[0].ImageUrl);
        Assert.Equal("24w01a", n[1].Title);
    }

    [Fact]
    public void VersionManifest_Parsing()
    {
        var m = VersionService.ParseManifest("""{"latest":{"release":"1.21","snapshot":"24w01a"},"versions":[{"id":"1.21","type":"release","url":"https://piston-meta.mojang.com/v1/x.json","sha1":"ab","releaseTime":"2024-06-13T08:32:38+00:00"}]}""");
        Assert.Equal("1.21", m[0].Id);
        Assert.Equal("release", m[0].Type);
    }
}

public class ModFolderTests
{
    [Fact]
    public void ListAndToggleMods()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(root, Path.Combine(root, "data"));
        var profile = new GameProfile { Name = "t", MinecraftVersion = "1.20.1", Loader = LoaderType.Fabric };
        var mods = paths.ModsDir(profile.Id);
        Directory.CreateDirectory(mods);
        using (var zip = ZipFile.Open(Path.Combine(mods, "sodium.jar"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            w.Write("""{"id":"sodium","name":"Sodium","version":"0.5.8"}""");
        File.WriteAllText(Path.Combine(mods, "notes.txt"), "x");

        var inst = new ModInstaller(paths, new FileDownloader(new SafeHttp(new HttpClient(new FakeHandler(_ => new HttpResponseMessage())))));
        var list = inst.List(profile);
        Assert.Equal(1, list.Count);
        Assert.Equal("Sodium", list[0].Title);
        Assert.Equal("0.5.8", list[0].Version);
        Assert.True(list[0].Enabled);

        inst.SetEnabled(profile, "sodium.jar", false);
        Assert.True(File.Exists(Path.Combine(mods, "sodium.jar.disabled")));
        Assert.False(inst.List(profile)[0].Enabled);
        inst.SetEnabled(profile, "sodium.jar.disabled", true);
        Assert.True(File.Exists(Path.Combine(mods, "sodium.jar")));
        inst.Delete(profile, "sodium.jar");
        Assert.Equal(0, inst.List(profile).Count);
    }
}
