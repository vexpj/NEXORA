using System.Diagnostics;
using System.Runtime.InteropServices;
using NEXORA.Core.Perf;

namespace NEXORA.Infrastructure.Perf;

/// <summary>Показатели игры: CPU и память процесса, память системы и (для NVIDIA) загрузка и температура GPU.</summary>
public sealed class PerformanceMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private readonly Process? _proc;
    private TimeSpan _lastCpu;
    private DateTime _lastWall = DateTime.UtcNow;
    private GpuInfo? _gpu;
    private DateTime _gpuAt = DateTime.MinValue;
    private string? _smi;
    private bool _smiSearched;

    public PerformanceMonitor(int pid)
    {
        try { _proc = Process.GetProcessById(pid); _lastCpu = _proc.TotalProcessorTime; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { _proc = null; }
    }

    /// <summary>null — процесс игры завершился.</summary>
    public async Task<PerfSample?> SampleAsync(CancellationToken ct)
    {
        if (_proc is null) return null;
        try
        {
            _proc.Refresh();
            if (_proc.HasExited) return null;
            var cpu = _proc.TotalProcessorTime; var now = DateTime.UtcNow;
            var pct = PerfMath.CpuPercent(cpu - _lastCpu, now - _lastWall, Environment.ProcessorCount);
            _lastCpu = cpu; _lastWall = now;
            var (used, total) = SystemMemory();
            await RefreshGpuAsync(ct);
            return new PerfSample(pct, (int)(_proc.WorkingSet64 / 1024 / 1024), used, total, _gpu);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private static (int usedPercent, int totalMb) SystemMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var m = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref m)) return ((int)m.dwMemoryLoad, (int)(m.ullTotalPhys / 1024 / 1024));
        }
        return (0, (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024));
    }

    private async Task RefreshGpuAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _gpuAt < TimeSpan.FromSeconds(2)) return;
        _gpuAt = DateTime.UtcNow;
        if (!_smiSearched)
        {
            _smiSearched = true;
            var candidates = new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            };
            _smi = candidates.FirstOrDefault(File.Exists);
        }
        if (_smi is null) return;
        try
        {
            var psi = new ProcessStartInfo(_smi, "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            _gpu = PerfMath.ParseNvidiaSmi(output) ?? _gpu;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or System.ComponentModel.Win32Exception) { }
    }

    public void Dispose() => _proc?.Dispose();
}
