using System.Diagnostics;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Diagnostics;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Mods;

namespace NEXORA.Infrastructure.Diagnostics;

/// <summary>Собирает журнал, crash-report и hs_err после вылета и отдаёт их анализатору.</summary>
public sealed class CrashDiagnosticService
{
    private readonly LauncherPaths _paths;
    public CrashDiagnosticService(LauncherPaths paths) => _paths = paths;

    public List<Diagnosis> Analyze(GameProfile profile, int exitCode, DateTime launchedUtc, string logPath, LauncherSettings settings)
    {
        var instance = _paths.InstanceDir(profile.Id);
        var ctx = new CrashContext
        {
            ExitCode = exitCode, LogText = ReadTail(logPath, 1_500_000) ?? "",
            CrashReportText = ReadNewest(Path.Combine(instance, "crash-reports"), "crash-*.txt", launchedUtc),
            JvmCrashText = ReadNewest(instance, "hs_err_pid*.log", launchedUtc),
            CurrentRamMb = profile.RamMb ?? settings.RamMb,
            PhysicalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            MinecraftVersion = profile.MinecraftVersion, Loader = profile.Loader,
            UsesCustomJava = !string.IsNullOrWhiteSpace(settings.JavaPath),
        };
        return CrashAnalyzer.Analyze(ctx);
    }

    private static string? ReadTail(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > maxBytes) fs.Seek(-maxBytes, SeekOrigin.End);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch (IOException) { return null; }
    }

    private static string? ReadNewest(string dir, string pattern, DateTime sinceUtc)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            var f = new DirectoryInfo(dir).GetFiles(pattern).Where(x => x.LastWriteTimeUtc >= sinceUtc.AddSeconds(-10))
                .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            return f is null ? null : ReadTail(f.FullName, 1_000_000);
        }
        catch (IOException) { return null; }
    }
}

public sealed record FixResult(bool ProfileChanged, string Message);

/// <summary>Применяет безопасные исправления. Ничего не удаляет из миров, сохранений и скриншотов.</summary>
public sealed class FixService
{
    private static readonly HashSet<string> UrlHosts = new(StringComparer.OrdinalIgnoreCase)
        { "www.nvidia.com", "www.amd.com", "www.intel.com" };

    private readonly LauncherPaths _paths;
    private readonly ISettingsStore _store;
    private readonly LauncherSettings _settings;
    private readonly ModInstaller _mods;

    public FixService(LauncherPaths paths, ISettingsStore store, LauncherSettings settings, ModInstaller mods)
    { _paths = paths; _store = store; _settings = settings; _mods = mods; }

    public FixResult Apply(FixAction fix, GameProfile profile)
    {
        switch (fix.Kind)
        {
            case FixKind.SetRam:
            {
                var ram = Math.Clamp(fix.IntValue, LauncherSettings.MinRamMb, LauncherSettings.MaxRamMb);
                var changed = false;
                if (profile.RamMb is not null) { profile.RamMb = ram; changed = true; }
                else { _settings.RamMb = ram; _store.Save(_settings); }
                return new(changed, $"Выделено {ram} МБ памяти. Запустите игру снова.");
            }
            case FixKind.ClearJavaPath:
                _settings.JavaPath = null; _store.Save(_settings);
                return new(false, "Своя Java сброшена: NEXORA подберёт подходящую сам. Запустите игру снова.");
            case FixKind.DisableMod:
                return new(false, DisableMod(profile, fix.Text ?? ""));
            case FixKind.RepairInstall:
                return Repair(profile);
            case FixKind.OpenModsFolder:
                Directory.CreateDirectory(_paths.ModsDir(profile.Id)); Open(_paths.ModsDir(profile.Id));
                return new(false, "Папка модов открыта.");
            case FixKind.OpenLogsFolder:
                Directory.CreateDirectory(_paths.LogsDir); Open(_paths.LogsDir);
                return new(false, "Папка журналов открыта.");
            case FixKind.OpenUrl:
                if (Uri.TryCreate(fix.Text, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && UrlHosts.Contains(u.Host)) { Open(u.AbsoluteUri); return new(false, "Страница открыта в браузере."); }
                return new(false, "Эта ссылка не разрешена.");
            default: return new(false, "Неизвестное действие.");
        }
    }

    private string DisableMod(GameProfile profile, string key)
    {
        if (key.Length < 2) return "Не указан мод.";
        var hits = _mods.List(profile).Where(m => m.Enabled && (
            string.Equals(m.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Title, key, StringComparison.OrdinalIgnoreCase) ||
            (key.Length >= 3 && m.FileName.Contains(key, StringComparison.OrdinalIgnoreCase)))).ToList();
        if (hits.Count == 0) return $"Не удалось найти файл мода «{key}». Отключите его вручную в папке модов.";
        foreach (var h in hits) _mods.SetEnabled(profile, h.FileName, false);
        return $"Отключено: {string.Join(", ", hits.Select(h => h.FileName))}. Включить обратно можно на вкладке «Моды».";
    }

    private FixResult Repair(GameProfile profile)
    {
        var natives = Path.Combine(_paths.VersionDir(profile.LaunchVersionId), "natives");
        if (Directory.Exists(natives)) Directory.Delete(natives, true);
        var changed = false;
        if (profile.Loader != LoaderType.Vanilla && !string.IsNullOrEmpty(profile.InstalledVersionId))
        {
            var dir = NEXORA.Infrastructure.Minecraft.GameInstaller.SafeCombine(_paths.Versions, profile.InstalledVersionId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            profile.InstalledVersionId = null; changed = true;
        }
        return new(changed, "Файлы подготовлены к восстановлению. Нажмите «Играть» — NEXORA проверит и докачает всё нужное. Миры и настройки не тронуты.");
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
