using System.Runtime.InteropServices;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Files;

/// <summary>
/// Sends paths to the Windows Recycle Bin via <c>SHFileOperation</c>. On non-Windows platforms
/// (or if the shell call fails) it falls back to moving the item into a managed trash folder under
/// the data root. See docs/04-filesystem-sync.md.
/// </summary>
public sealed class RecycleBin(IWorkspacePaths paths, ILogger<RecycleBin> logger) : IRecycleBin
{
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<RecycleBin> _logger = logger;

    public bool IsOsRecycleBinAvailable => OperatingSystem.IsWindows();

    public Task SendToRecycleBinAsync(string fullPath, CancellationToken ct = default)
    {
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return Task.CompletedTask;

        if (OperatingSystem.IsWindows() && TryShellRecycle(fullPath))
            return Task.CompletedTask;

        FallbackToTrash(fullPath);
        return Task.CompletedTask;
    }

    private bool TryShellRecycle(string fullPath)
    {
        try
        {
            // Double null-terminated list of paths.
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = fullPath + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };

            var result = SHFileOperation(ref op);
            if (result != 0 || op.fAnyOperationsAborted)
            {
                _logger.LogWarning("SHFileOperation returned {Code} for {Path}; using fallback trash", result, fullPath);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recycle Bin call failed for {Path}; using fallback trash", fullPath);
            return false;
        }
    }

    private void FallbackToTrash(string fullPath)
    {
        var trashRoot = Path.Combine(_paths.DataRoot, "Trash");
        Directory.CreateDirectory(trashRoot);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        var target = Path.Combine(trashRoot, $"{stamp}__{name}");

        if (Directory.Exists(fullPath))
            Directory.Move(fullPath, target);
        else
            File.Move(fullPath, target, overwrite: false);

        _logger.LogInformation("Moved {Path} to fallback trash at {Target}", fullPath, target);
    }

    // ---- Win32 interop ----
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
