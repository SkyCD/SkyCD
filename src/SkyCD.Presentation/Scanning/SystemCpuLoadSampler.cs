using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Cross-platform system CPU sampler: <c>GetSystemTimes</c> on Windows, <c>/proc/stat</c> on Linux and a
/// summed <see cref="Process.TotalProcessorTime"/> delta elsewhere. The first call primes the baseline
/// and returns zero.
/// </summary>
public sealed class SystemCpuLoadSampler : ICpuLoadSampler
{
    private readonly object gate = new();
    private CpuTimes previous;
    private DateTimeOffset previousTimestamp;
    private bool hasPrevious;

    public double Sample()
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var current = ReadTimes();
            var usage = 0d;
            var elapsed = (now - previousTimestamp).TotalSeconds;
            if (hasPrevious && elapsed > 0.02)
            {
                var totalDelta = current.Total - previous.Total;
                if (current.BusyOnly)
                {
                    var capacity = elapsed * Math.Max(1, Environment.ProcessorCount) * TimeSpan.TicksPerSecond;
                    usage = capacity > 0 ? totalDelta / capacity : 0;
                }
                else if (totalDelta > 0)
                {
                    var idleDelta = current.Idle - previous.Idle;
                    usage = 1 - Math.Clamp((double)idleDelta / totalDelta, 0, 1);
                }
            }

            previous = current;
            previousTimestamp = now;
            hasPrevious = true;
            return Math.Clamp(usage, 0, 1);
        }
    }

    private static CpuTimes ReadTimes()
    {
        if (OperatingSystem.IsWindows() && TryReadWindowsTimes(out var windows))
        {
            return windows;
        }

        if (OperatingSystem.IsLinux() && TryReadProcStat(out var linux))
        {
            return linux;
        }

        return ReadProcessTimes();
    }

    private static bool TryReadWindowsTimes(out CpuTimes times)
    {
        times = default;
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return false;
            }

            // Kernel time already contains idle time.
            times = new CpuTimes(kernel.ToUInt64() + user.ToUInt64(), idle.ToUInt64(), BusyOnly: false);
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryReadProcStat(out CpuTimes times)
    {
        times = default;
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                return false;
            }

            var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(part => ulong.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0UL)
                .ToArray();
            if (values.Length < 4)
            {
                return false;
            }

            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            var total = values.Aggregate(0UL, (sum, value) => sum + value);
            times = new CpuTimes(total, idle, BusyOnly: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static CpuTimes ReadProcessTimes()
    {
        ulong total = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                total += (ulong)process.TotalProcessorTime.Ticks;
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
                                                  or System.ComponentModel.Win32Exception)
            {
                // Processes exit between enumeration and sampling; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new CpuTimes(total, Idle: 0, BusyOnly: true);
    }

    private readonly record struct CpuTimes(ulong Total, ulong Idle, bool BusyOnly);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
}
