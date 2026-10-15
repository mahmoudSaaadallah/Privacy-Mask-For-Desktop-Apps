using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrivacyMask.Updates;

internal sealed record PipeProcessIdentity(int ProcessId, long CreationUtcTicks, string ExecutablePath)
{
    internal static PipeProcessIdentity Capture(Process process, string expectedPath)
    {
        // Retain the Process/SafeHandle in the caller until the exchange is finished.
        _ = process.SafeHandle;
        var identity = new PipeProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(expectedPath));
        identity.ValidateLive();
        return identity;
    }

    internal void ValidateLive()
    {
        if (!OperatingSystem.IsWindows() || ProcessId <= 0 || CreationUtcTicks <= 0 || !Path.IsPathFullyQualified(ExecutablePath))
            throw new ReleaseValidationException("Invalid update process identity.");
        StagingWorkspace.AssertSafe(ExecutablePath);
        using var actual = Process.GetProcessById(ProcessId);
        if (actual.HasExited || actual.StartTime.ToUniversalTime().Ticks != CreationUtcTicks ||
            !string.Equals(ImagePath(actual), ExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new ReleaseValidationException("Update process identity changed.");
    }

    private static unsafe string ImagePath(Process process)
    {
        // Module enumeration can be empty during loader startup. Query the retained process image instead.
        Span<char> buffer = stackalloc char[32768];
        uint count = (uint)buffer.Length;
        fixed (char* path = buffer)
            if (!QueryFullProcessImageName(process.SafeHandle, 0, path, ref count))
                throw new ReleaseValidationException("Unable to identify update process image.");
        return new string(buffer[..checked((int)count)]);
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* path, ref uint size);

    internal void ValidatePipe(PipeStream pipe, bool serverSide)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        uint pid;
        var success = serverSide ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid);
        if (!success || pid != ProcessId) throw new ReleaseValidationException("Unexpected update pipe peer.");
        ValidateLive();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
