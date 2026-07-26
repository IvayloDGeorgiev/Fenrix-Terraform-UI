using System.Runtime.InteropServices;
using System.Text;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// Stores secret values in the Windows Credential Manager (the OS-native, per-user encrypted store) via the
/// Win32 <c>Cred*</c> APIs. Fenrix persists only a <see cref="SecretReference"/> whose
/// <see cref="SecretReference.ReferenceKey"/> is the credential target name; the value never touches the
/// database, logs, or history. Reads for <see cref="SecretProvider.GitCredentialManager"/> resolve against
/// the same underlying store. See docs/11-secrets.md.
/// </summary>
public sealed class WindowsCredentialManagerStore(ILogger<WindowsCredentialManagerStore> logger)
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168; // ERROR_NOT_FOUND

    private readonly ILogger<WindowsCredentialManagerStore> _logger = logger;

    public static bool IsSupported => OperatingSystem.IsWindows();

    public void Store(SecretReference reference, string secretValue)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        if (string.IsNullOrEmpty(reference.ReferenceKey))
            throw new ArgumentException("A reference key (credential target) is required.", nameof(reference));

        var blob = Encoding.Unicode.GetBytes(secretValue ?? string.Empty);
        var blobPtr = IntPtr.Zero;
        try
        {
            blobPtr = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = reference.ReferenceKey,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = string.IsNullOrEmpty(reference.DisplayName) ? "Fenrix" : reference.DisplayName,
                Comment = "Managed by Fenrix IaC Studio"
            };

            if (!CredWriteW(ref cred, 0))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CredWrite failed for '{reference.ReferenceKey}' (Win32 {err}).");
            }
        }
        finally
        {
            if (blobPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(blobPtr);
        }
    }

    public string? Retrieve(SecretReference reference)
    {
        if (!IsSupported || string.IsNullOrEmpty(reference.ReferenceKey))
            return null;

        if (!CredReadW(reference.ReferenceKey, CredTypeGeneric, 0, out var handle))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ErrorNotFound)
                _logger.LogWarning("CredRead failed for a secret reference (Win32 {Error}).", err);
            return null;
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(handle);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return string.Empty;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Delete(SecretReference reference)
    {
        if (!IsSupported || string.IsNullOrEmpty(reference.ReferenceKey))
            return;

        if (!CredDeleteW(reference.ReferenceKey, CredTypeGeneric, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ErrorNotFound)
                _logger.LogWarning("CredDelete failed for a secret reference (Win32 {Error}).", err);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW([In] ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
