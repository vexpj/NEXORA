using NEXORA.Core.Models;

namespace NEXORA.Core.Abstractions;

public interface ISettingsStore
{
    LauncherSettings Load();
    void Save(LauncherSettings settings);
}
