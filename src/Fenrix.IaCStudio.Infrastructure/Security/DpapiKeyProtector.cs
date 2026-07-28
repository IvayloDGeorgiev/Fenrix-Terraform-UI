using System.Runtime.InteropServices;
using Fenrix.IaCStudio.Application.Abstractions.Security;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// Encrypts small local values at rest with the Windows Data Protection API (DPAPI) at per-user scope, via
/// the Win32 <c>CryptProtectData</c>/<c>CryptUnprotectData</c> functions (P/Invoke, no extra package). Used to
/// protect managed private-key files — item (6) of docs/11-secrets.md, docs/28-key-pair-management.md.
/// </summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public bool IsSupported => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        var inPtr = Marshal.AllocHGlobal(input.Length);
        var outBlob = new DATA_BLOB();
        try
        {
            Marshal.Copy(input, 0, inPtr, input.Length);
            var inBlob = new DATA_BLOB { cbData = input.Length, pbData = inPtr };

            var ok = protect
                ? CryptProtectData(ref inBlob, "Fenrix managed key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob);

            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"DPAPI {(protect ? "protect" : "unprotect")} failed (Win32 {err}).");
            }

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
