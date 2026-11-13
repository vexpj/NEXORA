using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NEXORA.Core.Abstractions;

namespace NEXORA.Infrastructure.Security;

/// <summary>Защита секретов средствами Windows (DPAPI): расшифровать их может только этот пользователь Windows на этом ПК.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pIn, string? descr, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob pOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pIn, IntPtr descr, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob pOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int UiForbidden = 0x1;

    public string Protect(string plain) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plain), true));
    public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedValue), false));

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
        Marshal.Copy(input, 0, inBlob.pbData, input.Length);
        try
        {
            var ok = protect
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var o1)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out o1);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[o1.cbData];
                Marshal.Copy(o1.pbData, result, 0, result.Length);
                return result;
            }
            finally { LocalFree(o1.pbData); }
        }
        finally { Marshal.FreeHGlobal(inBlob.pbData); }
    }
}
