using System.Globalization;

namespace NEXORA.Core.Perf;

public sealed record GpuInfo(string Name, int UtilPercent, int TempC, int MemUsedMb, int MemTotalMb);

/// <summary>FPS здесь отсутствует намеренно: Minecraft не отдаёт его наружу без мода-датчика.</summary>
public sealed record PerfSample(double CpuPercent, int ProcessRamMb, int SystemRamUsedPercent, int SystemRamTotalMb, GpuInfo? Gpu);

public static class PerfMath
{
    /// <summary>Загрузка процессора процессом в процентах от всей мощности компьютера.</summary>
    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int logicalCores)
    {
        if (wallDelta.TotalMilliseconds <= 0 || logicalCores <= 0) return 0;
        return Math.Clamp(cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * logicalCores) * 100.0, 0, 100);
    }

    /// <summary>Вывод «nvidia-smi --query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits».</summary>
    public static GpuInfo? ParseNvidiaSmi(string? output)
    {
        var line = output?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;
        var p = line.Split(',', StringSplitOptions.TrimEntries);
        if (p.Length < 5) return null;
        return int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) &&
               int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) &&
               int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mu) &&
               int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mt)
            ? new GpuInfo(p[0], u, t, mu, mt) : null;
    }

    /// <summary>Процесс игры занял почти весь лимит -Xmx — пора добавить память.</summary>
    public static bool NearMemoryLimit(int processRamMb, int xmxMb) => xmxMb > 0 && processRamMb > xmxMb * 0.92;
}
