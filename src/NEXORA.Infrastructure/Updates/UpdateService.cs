using System.Diagnostics;
using System.Net;
using NEXORA.Core.Updates;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Updates;

/// <summary>Проверка и безопасная загрузка обновлений лаунчера: GitHub Releases → SHA-256 → (если есть) цифровая подпись → установщик.</summary>
public sealed class UpdateService
{
    private readonly SafeHttp _http;
    private readonly FileDownloader _dl;
    private readonly string _tempRoot;
    private readonly Func<string, CancellationToken, Task<string?>> _signatureStatus;

    public UpdateService(SafeHttp updateHttp, string tempRoot, Func<string, CancellationToken, Task<string?>>? signatureStatus = null)
    {
        _http = updateHttp; _dl = new FileDownloader(updateHttp); _tempRoot = tempRoot;
        _signatureStatus = signatureStatus ?? AuthenticodeVerifier.GetStatusAsync;
    }

    public static SafeHttp CreateHttp() => new(null, UpdateHostPolicy.Require);

    /// <summary>null — новее ничего нет (или релизов ещё нет).</summary>
    public async Task<ReleaseInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"https://api.github.com/repos/{UpdateConfig.Repository}/releases/latest", ct,
            r => r.Headers.Accept.ParseAdd("application/vnd.github+json"));
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"GitHub вернул {(int)resp.StatusCode}.", null, resp.StatusCode);
        var info = ReleaseParser.Parse(await resp.Content.ReadAsStringAsync(ct), UpdateConfig.Repository);
        return info is not null && ReleaseParser.IsNewer(info.Version, current) ? info : null;
    }

    /// <summary>Скачивает установщик и проверяет его. Возвращает путь только к проверенному файлу; при любой проблеме файл удаляется.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct)
    {
        var hashText = await _http.GetStringAsync(release.HashUrl, ct);
        var sha = ReleaseParser.ParseSha256File(hashText, UpdateConfig.SetupAsset)
            ?? throw new InvalidDataException("В релизе нет корректной контрольной суммы — обновление отклонено.");

        var dir = Path.Combine(_tempRoot, "NEXORA-update");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"NEXORA-Setup-{release.Version}.exe");
        if (File.Exists(dest)) File.Delete(dest);

        await _dl.DownloadAsync(release.SetupUrl, dest, new FileHash("sha256", sha), ct, maxBytes: UpdateConfig.MaxInstallerBytes);
        try
        {
            var status = await _signatureStatus(dest, ct);
            if (status is not null && status != "Valid" && status != "NotSigned")
                throw new InvalidDataException($"Цифровая подпись установщика недействительна ({status}) — обновление отклонено.");
            if (UpdateConfig.RequireSignedInstaller && status != "Valid")
                throw new InvalidDataException("Установщик не подписан — обновление отклонено.");
        }
        catch { TryDelete(dest); throw; }
        return dest;
    }

    /// <summary>Запускает проверенный установщик (Windows покажет запрос прав администратора). После этого лаунчер нужно закрыть.</summary>
    public static void RunInstaller(string verifiedInstallerPath) =>
        Process.Start(new ProcessStartInfo(verifiedInstallerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });

    private static void TryDelete(string p) { try { File.Delete(p); } catch (IOException) { } }
}

public static class AuthenticodeVerifier
{
    /// <summary>«Valid», «NotSigned», «HashMismatch», «NotTrusted»… или null, если проверить не удалось.</summary>
    public static async Task<string?> GetStatusAsync(string path, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var escaped = path.Replace("'", "''");
            var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add($"(Get-AuthenticodeSignature -LiteralPath '{escaped}').Status");
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var output = (await p.StandardOutput.ReadToEndAsync(cts.Token)).Trim();
            await p.WaitForExitAsync(cts.Token);
            return output.Length is > 0 and < 40 ? output : null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { return null; }
    }
}
