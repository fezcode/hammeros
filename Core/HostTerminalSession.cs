using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Clockt.Terminal;
using Microsoft.Win32.SafeHandles;
using static HammerOS.Core.NativeMethods;

namespace HammerOS.Core;

/// <summary>A real, unelevated Windows ConPTY. Pipe I/O and teardown never block the UI.</summary>
public sealed class HostTerminalSession : IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _input, _output;
    private readonly SafeFileHandle _job;
    private readonly Channel<byte[]> _writes = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private IntPtr _console, _process;
    private int _disposed;
    private readonly Task _reader, _writer;
    public TerminalEmulator Emulator { get; }
    public int ProcessId { get; }
    public string ShellName { get; }
    public Task<int> Completion { get; }
    public bool HasExited => Completion.IsCompleted;

    public static string FindPowerShell()
    {
        var modern = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        return File.Exists(modern) ? modern : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    }
    public HostTerminalSession(int columns = 100, int rows = 28, string? workingDirectory = null, string shell = "powershell", string? initialCommand = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Host terminals require Windows 10 1809 or later.");
        if (initialCommand is not null && shell == "cmd") throw new ArgumentException("Initial commands use PowerShell.", nameof(initialCommand));
        Emulator = new TerminalEmulator(columns, rows, 5000);
        var executable = shell == "cmd" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : FindPowerShell();
        ShellName = shell == "cmd" ? "Command Prompt" : Path.GetFileNameWithoutExtension(executable) == "pwsh" ? "PowerShell 7" : "Windows PowerShell";
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null, job = null;
        IntPtr attributes = IntPtr.Zero, command = IntPtr.Zero, environment = IntPtr.Zero;
        var attributesReady = false;
        PROCESS_INFORMATION process = default;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
            if (!CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(new COORD { X = (short)columns, Y = (short)rows }, inputRead, outputWrite, 0, out _console));
            IntPtr bytes = IntPtr.Zero; InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes); attributes = Marshal.AllocHGlobal(bytes);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception(); attributesReady = true;
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            var startup = new STARTUPINFOEX { lpAttributeList = attributes }; startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>(); startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            job = CreateJobObjectW(IntPtr.Zero, null); if (job.IsInvalid) throw new Win32Exception();
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // Kill all owned descendants when the job closes.
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception();
            var arguments = shell == "cmd" ? "/D" : "-NoLogo -NoProfile";
            if (initialCommand is not null) arguments += " -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(initialCommand));
            command = Marshal.StringToHGlobalUni($"\"{executable}\" " + arguments);
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) if (entry.Key is string key && entry.Value is string value) variables[key] = value;
            variables["TERM"] = "xterm-256color"; variables["COLORTERM"] = "truecolor"; variables["TERM_PROGRAM"] = "HammerOS";
            environment = Marshal.StringToHGlobalUni(string.Join('\0', variables.Select(x => x.Key + "=" + x.Value)) + "\0\0");
            // Start suspended: no child can escape process ownership before assignment.
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | 0x4, environment,
                workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ref startup, out process)) throw new Win32Exception();
            if (!AssignProcessToJobObject(job, process.hProcess)) throw new Win32Exception();
            if (ResumeThread(process.hThread) == uint.MaxValue) throw new Win32Exception();
            _process = process.hProcess; ProcessId = process.dwProcessId; _job = job;
            _input = new FileStream(inputWrite, FileAccess.Write, 4096, false); _output = new FileStream(outputRead, FileAccess.Read, 16384, false);
        }
        catch
        {
            if (process.hProcess != IntPtr.Zero) { TerminateProcess(process.hProcess, 1); CloseHandle(process.hProcess); }
            job?.Dispose(); inputWrite?.Dispose(); outputRead?.Dispose();
            if (_console != IntPtr.Zero) { ClosePseudoConsole(_console); _console = IntPtr.Zero; }
            throw;
        }
        finally
        {
            inputRead?.Dispose(); outputWrite?.Dispose();
            if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
            if (attributesReady) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (command != IntPtr.Zero) Marshal.FreeHGlobal(command);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
        _reader = Task.Factory.StartNew(ReadOutput, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _writer = Task.Run(WriteInput);
        Completion = Task.Factory.StartNew(WaitForExit, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }
    public void Send(string text) => Send(Encoding.UTF8.GetBytes(text));
    public void Send(byte[] data) { if (Volatile.Read(ref _disposed) == 0 && data.Length > 0) _writes.Writer.TryWrite(data); }
    public void Resize(int columns, int rows)
    {
        columns = Math.Clamp(columns, 10, 400); rows = Math.Clamp(rows, 3, 200);
        lock (_gate)
        {
            if (_console == IntPtr.Zero || Volatile.Read(ref _disposed) != 0) return;
            Emulator.Resize(columns, rows); ResizePseudoConsole(_console, new COORD { X = (short)columns, Y = (short)rows });
        }
    }
    private void ReadOutput()
    {
        var buffer = new byte[16384];
        try
        {
            int count;
            while ((count = _output.Read(buffer, 0, buffer.Length)) > 0)
            {
                Emulator.Feed(buffer.AsSpan(0, count));
                var response = Emulator.TakeResponses(); if (response.Length > 0) Send(response);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
    private async Task WriteInput()
    {
        try { await foreach (var data in _writes.Reader.ReadAllAsync()) { _input.Write(data); _input.Flush(); } }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
    private async Task<int> WaitForExit()
    {
        WaitForSingleObject(_process, INFINITE); GetExitCodeProcess(_process, out var code);
        Interlocked.Exchange(ref _disposed, 1); _writes.Writer.TryComplete(); _job.Dispose();
        IntPtr console;
        lock (_gate) { console = _console; _console = IntPtr.Zero; }
        // Output keeps draining while ClosePseudoConsole emits its final frame.
        if (console != IntPtr.Zero) ClosePseudoConsole(console);
        await _reader; _input.Dispose(); await _writer; _output.Dispose();
        CloseHandle(_process); _process = IntPtr.Zero;
        return unchecked((int)code);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _writes.Writer.TryComplete(); _job.Dispose();
        // The waiter owns remaining native handles and drains output to EOF.
    }

    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
}
