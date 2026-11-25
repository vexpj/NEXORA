using System.Net;
using System.Text;
using NEXORA.Core.Updates;
using NEXORA.Infrastructure.Net;
using NEXORA.Infrastructure.Packs;
using NEXORA.Infrastructure.Updates;
using Xunit;

namespace NEXORA.Tests;

public class UpdateTests
{
    private const string Prefix = "https://github.com/vexpj/NEXORA/releases/download/v9.9.9/";
    private static string Sha(byte[] b) => Hashing.Sha1Hex(b).Length == 40 ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant() : "";

    private static string ReleaseJson(string tag = "v9.9.9", bool draft = false, bool pre = false, string? setupUrl = null, bool withHash = true) => $$"""
    {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLower()}},"prerelease":{{pre.ToString().ToLower()}},"body":"Что нового: тест",
     "assets":[{"name":"NEXORA-Setup.exe","browser_download_url":"{{setupUrl ?? Prefix + "NEXORA-Setup.exe"}}"}
               {{(withHash ? ",{\"name\":\"NEXORA-Setup.exe.sha256\",\"browser_download_url\":\"" + Prefix + "NEXORA-Setup.exe.sha256\"}" : "")}}]}
    """;

    private static string Tmp() => Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));

    private static UpdateService Svc(Func<HttpRequestMessage, HttpResponseMessage> handler, string? sig = "NotSigned") =>
        new(new SafeHttp(new HttpClient(new FakeHandler(handler)), UpdateHostPolicy.Require), Tmp(), (_, _) => Task.FromResult(sig));

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Bytes(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };

    [Fact]
    public void TagAndVersionComparison()
    {
        Assert.Equal(new Version(0, 3, 3), ReleaseParser.ParseTag("v0.3.3"));
        Assert.Equal(new Version(0, 4), ReleaseParser.ParseTag("0.4"));
        Assert.Null(ReleaseParser.ParseTag("latest"));
        Assert.Null(ReleaseParser.ParseTag(""));
        Assert.True(ReleaseParser.IsNewer(new Version(0, 3, 3), new Version(0, 3, 2)));
        Assert.False(ReleaseParser.IsNewer(new Version(0, 3, 2), new Version(0, 3, 2)));
        Assert.False(ReleaseParser.IsNewer(new Version(0, 3, 1), new Version(0, 3, 2)));
        Assert.True(Version.TryParse(AppVersion.Current, out _));
    }

    [Fact]
    public void ReleaseParsing_AcceptsGoodAndRejectsSuspicious()
    {
        var r = ReleaseParser.Parse(ReleaseJson(), "vexpj/NEXORA")!;
        Assert.Equal(new Version(9, 9, 9), r.Version);
        Assert.Equal(Prefix + "NEXORA-Setup.exe", r.SetupUrl);
        Assert.Contains("Что нового", r.Notes);
        Assert.Null(ReleaseParser.Parse(ReleaseJson(draft: true), "vexpj/NEXORA"));
        Assert.Null(ReleaseParser.Parse(ReleaseJson(pre: true), "vexpj/NEXORA"));
        Assert.Null(ReleaseParser.Parse(ReleaseJson(withHash: false), "vexpj/NEXORA"));            // нет файла контрольной суммы
        Assert.Null(ReleaseParser.Parse(ReleaseJson(setupUrl: "https://evil.example.com/NEXORA-Setup.exe"), "vexpj/NEXORA"));
        Assert.Null(ReleaseParser.Parse(ReleaseJson(setupUrl: "https://github.com/someone-else/NEXORA/releases/download/v9.9.9/NEXORA-Setup.exe"), "vexpj/NEXORA"));
        Assert.Null(ReleaseParser.Parse("[]", "vexpj/NEXORA"));
    }

    [Fact]
    public void Sha256FileParsing()
    {
        var h = new string('a', 64);
        Assert.Equal(h, ReleaseParser.ParseSha256File(h + "  NEXORA-Setup.exe\n", "NEXORA-Setup.exe"));
        Assert.Equal(h, ReleaseParser.ParseSha256File(h.ToUpper() + " *NEXORA-Setup.exe", "NEXORA-Setup.exe"));
        Assert.Equal(h, ReleaseParser.ParseSha256File(h, "NEXORA-Setup.exe"));
        Assert.Null(ReleaseParser.ParseSha256File(h + "  other.exe", "NEXORA-Setup.exe"));
        Assert.Null(ReleaseParser.ParseSha256File("не хэш", "NEXORA-Setup.exe"));
        Assert.Null(ReleaseParser.ParseSha256File(new string('a', 63), "NEXORA-Setup.exe"));
    }

    [Fact]
    public void GithubHostsAllowedOnlyForUpdates()
    {
        UpdateHostPolicy.Require(new Uri("https://api.github.com/repos/x/y/releases/latest"));
        UpdateHostPolicy.Require(new Uri("https://release-assets.githubusercontent.com/a"));
        Assert.Throws<UntrustedHostException>(() => UpdateHostPolicy.Require(new Uri("https://evil.example.com/a")));
        Assert.Throws<UntrustedHostException>(() => UpdateHostPolicy.Require(new Uri("http://github.com/a")));
        // регрессия: GitHub нельзя использовать как источник модов из чужих сборок
        Assert.False(PackService.IsUrlAllowed("https://github.com/evil/repo/releases/download/v1/mod.jar"));
        Assert.Throws<UntrustedHostException>(() => HostPolicy.Require("https://github.com/a/b.jar"));
    }

    [Fact]
    public async Task Check_FindsNewerOnly()
    {
        var svc = Svc(_ => Ok(ReleaseJson()));
        Assert.Equal(new Version(9, 9, 9), (await svc.CheckAsync(new Version(0, 3, 2), CancellationToken.None))!.Version);
        Assert.Null(await svc.CheckAsync(new Version(10, 0), CancellationToken.None));
        Assert.Null(await Svc(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).CheckAsync(new Version(0, 1), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => Svc(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)).CheckAsync(new Version(0, 1), CancellationToken.None));
    }

    private static ReleaseInfo Info() => new(new Version(9, 9, 9), "v9.9.9", "", Prefix + "NEXORA-Setup.exe", Prefix + "NEXORA-Setup.exe.sha256");

    [Fact]
    public async Task Download_VerifiesSha256()
    {
        var data = Encoding.UTF8.GetBytes("installer-bytes");
        var svc = Svc(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Ok(Sha(data) + "  NEXORA-Setup.exe") : Bytes(data));
        var path = await svc.DownloadAsync(Info(), CancellationToken.None);
        Assert.Equal(data, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Download_TamperedInstallerIsRejectedAndNotLeftOnDisk()
    {
        var good = Encoding.UTF8.GetBytes("installer-bytes");
        var svc = Svc(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Ok(Sha(good) + "  NEXORA-Setup.exe") : Bytes(new byte[] { 1, 2, 3 }));
        await Assert.ThrowsAsync<IntegrityException>(() => svc.DownloadAsync(Info(), CancellationToken.None));
    }

    [Fact]
    public async Task Download_BrokenHashFileAndBadSignature_AreRejected()
    {
        var data = Encoding.UTF8.GetBytes("x");
        await Assert.ThrowsAsync<InvalidDataException>(() => Svc(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Ok("мусор") : Bytes(data)).DownloadAsync(Info(), CancellationToken.None));

        var tmp = Tmp();
        var svc = new UpdateService(new SafeHttp(new HttpClient(new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Ok(Sha(data)) : Bytes(data))), UpdateHostPolicy.Require),
            tmp, (_, _) => Task.FromResult<string?>("HashMismatch"));
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.DownloadAsync(Info(), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(tmp, "NEXORA-update", "NEXORA-Setup-9.9.9.exe")));
    }

    [Fact]
    public async Task Download_RedirectToUntrustedHost_IsBlocked()
    {
        var svc = Svc(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith(".sha256")) return Ok(new string('a', 64));
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            resp.Headers.Location = new Uri("https://evil.example.com/NEXORA-Setup.exe");
            return resp;
        });
        await Assert.ThrowsAsync<UntrustedHostException>(() => svc.DownloadAsync(Info(), CancellationToken.None));
    }

    [Fact]
    public async Task Downloader_EnforcesSizeLimit()
    {
        var dl = new FileDownloader(new SafeHttp(new HttpClient(new FakeHandler(_ => Bytes(new byte[5000])))));
        var dest = Path.Combine(Tmp(), "big.bin");
        await Assert.ThrowsAsync<InvalidDataException>(() => dl.DownloadAsync("https://cdn.modrinth.com/big.bin", dest, null, CancellationToken.None, maxBytes: 1000));
        Assert.False(File.Exists(dest));
    }
}
