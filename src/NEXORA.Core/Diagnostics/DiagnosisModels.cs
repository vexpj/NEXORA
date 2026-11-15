using NEXORA.Core.Models;

namespace NEXORA.Core.Diagnostics;

public enum FixKind { SetRam, ClearJavaPath, DisableMod, RepairInstall, OpenModsFolder, OpenLogsFolder, OpenUrl }
public enum Severity { Error, Warning, Info }

/// <summary>Безопасное исправление, которое пользователь запускает кнопкой. Все они обратимы и не трогают миры.</summary>
public sealed record FixAction(FixKind Kind, string Label, int IntValue = 0, string? Text = null);

public sealed record Diagnosis(string Id, string Title, string Explanation, Severity Severity, IReadOnlyList<FixAction> Fixes);

public sealed class CrashContext
{
    public int ExitCode { get; set; }
    public string LogText { get; set; } = "";
    public string? CrashReportText { get; set; }
    public string? JvmCrashText { get; set; }
    public int CurrentRamMb { get; set; } = 4096;
    public long PhysicalMemoryMb { get; set; }
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; }
    public bool UsesCustomJava { get; set; }
}
