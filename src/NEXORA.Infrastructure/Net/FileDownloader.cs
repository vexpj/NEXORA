using System.Security.Cryptography;

namespace NEXORA.Infrastructure.Net;

public sealed record FileHash(string Algorithm, string Hex);

public sealed class IntegrityException : IOException
{
    public IntegrityException(string path) : base($"Файл повреждён или подменён (контрольная сумма не совпала): {Path.GetFileName(path)}") { }
}

public static class Hashing
{
    public static async Task<string> ComputeHexAsync(string path, string algorithm, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        HashAlgorithm h = algorithm.ToLowerInvariant() switch
        {
            "sha1" => SHA1.Create(), "sha256" => SHA256.Create(), "sha512" => SHA512.Create(), "md5" => MD5.Create(),
            _ => throw new NotSupportedException(algorithm),
        };
        using (h) return Convert.ToHexString(await h.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    public static string Sha1Hex(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
}

public sealed class FileDownloader
{
    private readonly SafeHttp _http;
    public FileDownloader(SafeHttp http) => _http = http;

    public async Task DownloadAsync(string url, string dest, FileHash? hash, CancellationToken ct, int retries = 3)
    {
        if (hash is not null && File.Exists(dest) &&
            string.Equals(await Hashing.ComputeHexAsync(dest, hash.Algorithm, ct), hash.Hex, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".part";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var dst = File.Create(tmp);
                    await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                }
                if (hash is not null &&
                    !string.Equals(await Hashing.ComputeHexAsync(tmp, hash.Algorithm, ct), hash.Hex, StringComparison.OrdinalIgnoreCase))
                    throw new IntegrityException(dest);
                File.Move(tmp, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < retries && ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(tmp)) try { File.Delete(tmp); } catch (IOException) { }
            }
        }
    }
}
