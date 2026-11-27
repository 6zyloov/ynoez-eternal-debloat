using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace ynoez.Services;

public sealed record SystemMetricsSnapshot(
    DateTimeOffset Timestamp, double? CpuPercent, double MemoryUsedGb, double MemoryTotalGb,
    double MemoryPercent, double? Gpu3DPercent, double? GpuTemperatureC,
    double? GpuMemoryUsedMb, double? GpuMemoryTotalMb, double? GpuPowerWatts, string GpuTelemetrySource);

public sealed class PerformanceMonitorService
{
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;

    public Task<SystemMetricsSnapshot> SampleAsync() => Task.Run(Sample);

    private SystemMetricsSnapshot Sample()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleNow = idle.ToUInt64();
            var kernelNow = kernel.ToUInt64();
            var userNow = user.ToUInt64();
            if (_previousIdle.HasValue && _previousKernel.HasValue && _previousUser.HasValue)
            {
                var idleDelta = idleNow - _previousIdle.Value;
                var total = (kernelNow - _previousKernel.Value) + (userNow - _previousUser.Value);
                if (total > 0) cpu = Math.Clamp(100d * (total - idleDelta) / total, 0, 100);
            }
            _previousIdle = idleNow;
            _previousKernel = kernelNow;
            _previousUser = userNow;
        }

        var memory = GetMemory();
        var nvidia = QueryNvidiaTelemetry();
        var gpu3d = nvidia.Utilization ?? QueryGpu3DEngine();
        var source = nvidia.Utilization.HasValue ? "NVIDIA SMI" : gpu3d.HasValue ? "Windows GPU Engine" : "Indisponível";
        return new SystemMetricsSnapshot(DateTimeOffset.Now, cpu, memory.UsedGb, memory.TotalGb, memory.Percent,
            gpu3d, nvidia.Temperature, nvidia.MemoryUsed, nvidia.MemoryTotal, nvidia.Power, source);
    }

    private static (double UsedGb, double TotalGb, double Percent) GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return (0, 0, 0);
        var used = status.TotalPhysical - status.AvailablePhysical;
        const double gb = 1024d * 1024d * 1024d;
        return (used / gb, status.TotalPhysical / gb, 100d * used / status.TotalPhysical);
    }

    private static double? QueryGpu3DEngine()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            double? maximum = null;
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var name = Convert.ToString(item["Name"]) ?? "";
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    if (double.TryParse(Convert.ToString(item["UtilizationPercentage"]), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var value))
                        maximum = Math.Max(maximum ?? 0, Math.Clamp(value, 0, 100));
                }
            }
            return maximum;
        }
        catch { return null; }
    }

    private static NvidiaTelemetry QueryNvidiaTelemetry()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("nvidia-smi.exe",
                    "--query-gpu=temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(1800))
            {
                process.Kill(entireProcessTree: true);
                return NvidiaTelemetry.Empty;
            }
            var output = outputTask.GetAwaiter().GetResult().Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            if (process.ExitCode != 0 || output.Length == 0) return NvidiaTelemetry.Empty;
            var fields = output.Split(',').Select(x => x.Trim()).ToArray();
            if (fields.Length < 5) return NvidiaTelemetry.Empty;
            return new NvidiaTelemetry(Parse(fields[0]), Parse(fields[1]), Parse(fields[2]), Parse(fields[3]), Parse(fields[4]));
        }
        catch { return NvidiaTelemetry.Empty; }
    }

    private static double? Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private sealed record NvidiaTelemetry(double? Temperature, double? Utilization, double? MemoryUsed, double? MemoryTotal, double? Power)
    {
        public static NvidiaTelemetry Empty { get; } = new(null, null, null, null, null);
    }
}
