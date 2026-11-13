using NEXORA.Core.Models;

namespace NEXORA.Core.Abstractions;

public interface ISettingsStore { LauncherSettings Load(); void Save(LauncherSettings settings); }

public interface IProfileStore { List<GameProfile> Load(); void Save(IEnumerable<GameProfile> profiles); }

public interface ISecretProtector { string Protect(string plain); string Unprotect(string protectedValue); }

public sealed record StoredAccount(string RefreshToken);

public interface IAccountStore { StoredAccount? Load(); void Save(StoredAccount account); void Clear(); }
