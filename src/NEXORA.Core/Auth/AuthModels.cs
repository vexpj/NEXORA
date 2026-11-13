namespace NEXORA.Core.Auth;

public sealed record SkinInfo(string Id, string State, string Url, string Variant);
public sealed record CapeInfo(string Id, string State, string Alias, string Url);

public sealed class PlayerProfile
{
    public string Id { get; set; } = "";           // 32 hex символа
    public string Name { get; set; } = "";
    public List<SkinInfo> Skins { get; set; } = new();
    public List<CapeInfo> Capes { get; set; } = new();

    public SkinInfo? ActiveSkin => Skins.FirstOrDefault(s => s.State == "ACTIVE") ?? Skins.FirstOrDefault();
    public CapeInfo? ActiveCape => Capes.FirstOrDefault(c => c.State == "ACTIVE");
}

public sealed class MinecraftSession
{
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = "";
    public PlayerProfile Profile { get; set; } = new();
    public bool IsExpiringSoon => DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-2);
}

public class AuthException : Exception { public AuthException(string message, Exception? inner = null) : base(message, inner) { } }
