using System.Text.Json.Serialization;

namespace NEXORA.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoaderType { Vanilla, Fabric, Forge, NeoForge }

/// <summary>Игровой профиль (сборка): версия, загрузчик и отдельная папка.</summary>
public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Vanilla;
    public string? LoaderVersion { get; set; }
    /// <summary>Идентификатор установленной версии загрузчика (папка в versions/).</summary>
    public string? InstalledVersionId { get; set; }
    public int? RamMb { get; set; }
    public string? JvmArgs { get; set; }

    [JsonIgnore] public string LaunchVersionId => InstalledVersionId ?? MinecraftVersion;
    [JsonIgnore] public string Display =>
        Loader == LoaderType.Vanilla ? $"{Name}  ·  {MinecraftVersion}" : $"{Name}  ·  {MinecraftVersion} {Loader}";
}
