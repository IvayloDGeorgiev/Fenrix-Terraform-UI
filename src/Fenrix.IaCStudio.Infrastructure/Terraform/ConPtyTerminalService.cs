using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Embedded terminal backed by the Windows pseudo-console (ConPTY) API (Phase 12). Launches a shell attached to
/// a pseudo-console so any command — including interactive ones — can run with full VT output. Windows 10 1809+
/// only; elsewhere <see cref="IsSupported"/> is false and <see cref="Start"/> throws.
///
/// <para><b>Needs a build/run in Visual Studio to validate</b> — the P/Invoke + pipe/read-loop plumbing can't be
/// exercised in the authoring sandbox. It follows the canonical ConPTY pattern
/// (CreatePseudoConsole → STARTUPINFOEX with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE → CreateProcess).</para>
///
/// <para>The terminal inherits the app's environment, so it uses the same native cloud credential stores
/// (az/aws/gcloud) as the rest of Fenrix (docs/10). It is a full shell and bypasses the typed previews by
/// design — that is the point of a catch-all terminal.</para>
/// </summary>
public sealed class ConPtyTerminalService(ILogger<ConPtyTerminalService> logger) : ITerminalService
{
    private readonly ILogger<ConPtyTerminalService> _logger = logger;

    public bool IsSupported => OperatingSystem.IsWindows();

    public ITerminalSession Start(TerminalStartInfo info)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The embedded terminal requires Windows 10 (1809) or later.");
        return new ConPtySession(info, _logger);
    }

    [SupportedOSPlatform("windows")]
    private sealed class ConPtySession : ITerminalSession
    {
        private readonly ILogger _logger;
        private readonly object _gate = new();

        private IntPtr _pseudoConsole = IntPtr.Zero;
        private IntPtr _inputWrite = IntPtr.Zero;   // we write user input here
        private IntPtr _outputRead = IntPtr.Zero;   // we read shell output here
        private IntPtr _attrList = IntPtr.Zero;
        private FileStream? _writer;
        private FileStream? _reader;
        private Native.PROCESS_INFORMATION _pi;
        private volatile bool _running;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsRunning => _running;
        public event Action<string>? Output;
        public event Action<int>? Exited;

        public ConPtySession(TerminalStartInfo info, ILogger logger)
        {
            _logger = logger;
            Startup(info);
        }

        private void Startup(TerminalStartInfo info)
        {
            // 1) Two pipes: one for input to the console, one for its output.
            if (!Native.CreatePipe(out var inputRead, out _inputWrite, IntPtr.Zero, 0) ||
                !Native.CreatePipe(out _outputRead, out var outputWrite, IntPtr.Zero, 0))
                throw new InvalidOperationException("Failed to create terminal pipes.");

            // 2) The pseudo-console reads from inputRead and writes to outputWrite.
            var size = new Native.COORD { X = ClampCell(info.Columns, 80), Y = ClampCell(info.Rows, 24) };
            var hr = Native.CreatePseudoConsole(size, inputRead, outputWrite, 0, out _pseudoConsole);
            if (hr != 0)
                throw new InvalidOperationException($"CreatePseudoConsole failed (0x{hr:X8}).");

            // The console now owns these ends; close our copies.
            Native.CloseHandle(inputRead);
            Native.CloseHandle(outputWrite);

            // 3) Start the shell attached to the pseudo-console via a proc-thread attribute list.
            StartProcess(info);

            // 4) Wrap our ends as streams and begin pumping.
            _writer = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(_inputWrite, ownsHandle: false), FileAccess.Write);
            _reader = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(_outputRead, ownsHandle: false), FileAccess.Read);
            _running = true;

            _ = Task.Run(ReadLoopAsync);
            _ = Task.Run(WatchExit);
        }

        private void StartProcess(TerminalStartInfo info)
        {
            var startupInfo = new Native.STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<Native.STARTUPINFOEX>();

            // Size the attribute list, allocate, initialise, then set the pseudo-console attribute.
            var lpSize = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
            _attrList = Marshal.AllocHGlobal(lpSize);
            startupInfo.lpAttributeList = _attrList;

            if (!Native.InitializeProcThreadAttributeList(_attrList, 1, 0, ref lpSize))
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed.");

            if (!Native.UpdateProcThreadAttributeList(
                    _attrList, 0, Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _pseudoConsole, Marshal.SizeOf<IntPtr>(), IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttributeList failed.");

            var commandLine = new StringBuilder(info.Shell);
            var workingDir = string.IsNullOrWhiteSpace(info.WorkingDirectory) ? null : info.WorkingDirectory;

            var ok = Native.CreateProcess(
                null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                Native.EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, workingDir,
                ref startupInfo, out _pi);

            if (!ok)
                throw new InvalidOperationException($"Failed to start the terminal shell '{info.Shell}' (Win32 {Marshal.GetLastWin32Error()}).");
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[4096];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[8192];
            try
            {
                while (_running)
                {
                    var read = await _reader!.ReadAsync(buffer.AsMemory(0, buffer.Length));
                    if (read <= 0) break;
                    var count = decoder.GetChars(buffer, 0, read, chars, 0);
                    if (count > 0)
                        Output?.Invoke(new string(chars, 0, count));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Pipe closed on shell exit — normal teardown.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Terminal read loop ended unexpectedly.");
            }
        }

        private void WatchExit()
        {
            try
            {
                Native.WaitForSingleObject(_pi.hProcess, Native.INFINITE);
                Native.GetExitCodeProcess(_pi.hProcess, out var code);
                _running = false;
                Exited?.Invoke(code);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Terminal exit watcher failed.");
            }
        }

        public void Write(string data)
        {
            if (!_running || _writer is null || string.IsNullOrEmpty(data)) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                lock (_gate)
                {
                    _writer.Write(bytes, 0, bytes.Length);
                    _writer.Flush();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Terminal write failed.");
            }
        }

        public void Resize(int columns, int rows)
        {
            if (_pseudoConsole == IntPtr.Zero) return;
            var size = new Native.COORD { X = ClampCell(columns, 80), Y = ClampCell(rows, 24) };
            Native.ResizePseudoConsole(_pseudoConsole, size);
        }

        private static short ClampCell(int value, int fallback) =>
            (short)Math.Clamp(value <= 0 ? fallback : value, 1, short.MaxValue);

        public void Dispose()
        {
            _running = false;
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }

            if (_pseudoConsole != IntPtr.Zero) { Native.ClosePseudoConsole(_pseudoConsole); _pseudoConsole = IntPtr.Zero; }

            try
            {
                if (_pi.hProcess != IntPtr.Zero) Native.CloseHandle(_pi.hProcess);
                if (_pi.hThread != IntPtr.Zero) Native.CloseHandle(_pi.hThread);
            }
            catch { }

            if (_attrList != IntPtr.Zero)
            {
                Native.DeleteProcThreadAttributeList(_attrList);
                Marshal.FreeHGlobal(_attrList);
                _attrList = IntPtr.Zero;
            }

            if (_inputWrite != IntPtr.Zero) { Native.CloseHandle(_inputWrite); _inputWrite = IntPtr.Zero; }
            if (_outputRead != IntPtr.Zero) { Native.CloseHandle(_outputRead); _outputRead = IntPtr.Zero; }
        }
    }

    /// <summary>Minimal ConPTY / process P/Invoke surface (kernel32).</summary>
    [SupportedOSPlatform("windows")]
    private static class Native
    {
        public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        public static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
        public const uint INFINITE = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        public struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttributeList(
            IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(
            string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);
    }
}
