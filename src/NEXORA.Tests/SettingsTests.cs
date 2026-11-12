using NEXORA.Core.Models;
using NEXORA.Infrastructure.Settings;
using Xunit;

namespace NEXORA.Tests;

public class SettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Normalize_ClampsRam()
    {
        Assert.Equal(LauncherSettings.MinRamMb, new LauncherSettings { RamMb = 10 }.Normalize().RamMb);
        Assert.Equal(LauncherSettings.MaxRamMb, new LauncherSettings { RamMb = 999999 }.Normalize().RamMb);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new JsonSettingsStore(TempFile()).Load();
        Assert.Equal(LauncherSettings.DefaultRamMb, s.RamMb);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(TempFile());
        store.Save(new LauncherSettings { RamMb = 6144, GameDirectory = "C:\\Games\\mc", JavaPath = "C:\\java\\bin\\javaw.exe" });
        var s = store.Load();
        Assert.Equal(6144, s.RamMb);
        Assert.Equal("C:\\Games\\mc", s.GameDirectory);
        Assert.Equal("C:\\java\\bin\\javaw.exe", s.JavaPath);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(LauncherSettings.DefaultRamMb, new JsonSettingsStore(path).Load().RamMb);
    }
}
