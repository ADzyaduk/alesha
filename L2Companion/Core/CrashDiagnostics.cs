using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace L2Companion.Core;

internal static class CrashDiagnostics
{
    private static readonly object Gate = new();

    public static void WriteUnhandled(string source, Exception? ex)
    {
        try
        {
            var dir = EnsureCrashDir();
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var baseName = $"{stamp}-{source}";
            var logPath = Path.Combine(dir, baseName + ".log");
            var dumpPath = Path.Combine(dir, baseName + ".dmp");

            var lines = new List<string>
            {
                $"time={DateTime.Now:O}",
                $"source={source}",
                $"process={Environment.ProcessId}",
                $"thread={Environment.CurrentManagedThreadId}",
                $"os={Environment.OSVersion}",
                $"clr={Environment.Version}",
                $"baseDir={AppDomain.CurrentDomain.BaseDirectory}"
            };

            if (ex is not null)
            {
                lines.Add("exception=" + ex.GetType().FullName);
                lines.Add("message=" + ex.Message);
                lines.Add("stack=");
                lines.Add(ex.ToString());
            }

            lock (Gate)
            {
                File.WriteAllLines(logPath, lines);
            }

            TryWriteMiniDump(dumpPath);
        }
        catch
        {
            // Last-resort path; do not throw from crash handler.
        }
    }

    public static void WriteMarker(string category, string message)
    {
        try
        {
            var dir = EnsureCrashDir();
            var path = Path.Combine(dir, "runtime-diagnostics.log");
            var line = $"{DateTime.Now:O} [{category}] {message}";
            lock (Gate)
            {
                File.AppendAllLines(path, [line]);
            }
        }
        catch
        {
            // ignore diagnostics failures
        }
    }

    private static string EnsureCrashDir()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "crash");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryWriteMiniDump(string dumpPath)
    {
        using var process = Process.GetCurrentProcess();
        using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None);

        _ = MiniDumpWriteDump(
            process.Handle,
            process.Id,
            fs.SafeFileHandle,
            MinidumpType.MiniDumpWithThreadInfo | MinidumpType.MiniDumpWithUnloadedModules,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    [Flags]
    private enum MinidumpType : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpFilterMemory = 0x00000008,
        MiniDumpScanMemory = 0x00000010,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
        MiniDumpFilterModulePaths = 0x00000080,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithPrivateReadWriteMemory = 0x00000200,
        MiniDumpWithoutOptionalData = 0x00000400,
        MiniDumpWithFullMemoryInfo = 0x00000800,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithCodeSegs = 0x00002000
    }

    [DllImport("Dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        int processId,
        SafeFileHandle hFile,
        MinidumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);
}
