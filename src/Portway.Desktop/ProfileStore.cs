using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portway.Core;

namespace Portway.Desktop;

/// <summary>사이트·설정과 암호화 Vault를 전용 프로필 폴더에서 관리합니다.</summary>
public sealed class ProfileStore : IDisposable
{
    readonly object gate = new();
    readonly string root;
    byte[]? key;
    bool automaticUnlock;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    record Vault(string Salt, string Check);
    public ProfileStore(IConfiguration config)
    {
        root = config["Portway:DataPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Portway");
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var restored = VaultAutoUnlock.Restore(root);
        if (restored != null)
        {
            try
            {
                var vault = Read<Vault?>("vault.json", null);
                if (vault != null && Read<List<Site>>("sites.json", []).Any(s => s.SavePassword && SiteSecrets.HasAny(s)) && Decrypt(restored, vault.Check) == "portway-vault-v1")
                {
                    key = restored;
                    automaticUnlock = true;
                }
                else
                    CryptographicOperations.ZeroMemory(restored);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException or ArgumentException)
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
    }

    T Read<T>(string name, T fallback) => File.Exists(Path.Combine(root, name)) ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root, name)), Json)! : fallback;
    void Write<T>(string name, T value)
    {
        var file = Path.Combine(root, name);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(file + ".tmp", file, true);
    }

    static string Encrypt(byte[] secret, string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(secret, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    static string Decrypt(byte[] secret, string value)
    {
        var data = Convert.FromBase64String(value);
        if (data.Length < 28)
            throw new CryptographicException("Vault 암호문이 올바르지 않습니다.");
        var bytes = new byte[data.Length - 28];
        using var aes = new AesGcm(secret, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), bytes);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public object Status()
    {
        lock (gate)
            return new
            {
                configured = File.Exists(Path.Combine(root, "vault.json")),
                unlocked = key != null,
                automaticUnlock
            };
    }

    public void Unlock(string password)
    {
        lock (gate)
        {
            var vault = Read<Vault?>("vault.json", null);
            if (vault == null && password.Length < 12)
                throw new ArgumentException("새 마스터 비밀번호는 12자 이상이어야 합니다.");
            var salt = vault == null ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(vault.Salt);
            var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
            try
            {
                if (vault == null)
                    Write("vault.json", new Vault(Convert.ToBase64String(salt), Encrypt(derived, "portway-vault-v1")));
                else if (Decrypt(derived, vault.Check) != "portway-vault-v1")
                    throw new CryptographicException();
                var hasSavedPassword = Read<List<Site>>("sites.json", []).Any(s => s.SavePassword && SiteSecrets.HasAny(s));
                ClearKey();
                key = derived;
                automaticUnlock = hasSavedPassword && VaultAutoUnlock.Store(root, key);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(derived);
                throw new ArgumentException("마스터 비밀번호가 올바르지 않습니다.");
            }
        }
    }

    public void Lock()
    {
        lock (gate)
        {
            ClearKey();
            VaultAutoUnlock.Remove(root);
            automaticUnlock = false;
        }
    }

    void ClearKey()
    {
        if (key != null)
            CryptographicOperations.ZeroMemory(key);
        key = null;
    }

    public Site[] List()
    {
        lock (gate)
            return Read<List<Site>>("sites.json", []).Select(SiteSecrets.Public).ToArray();
    }

    public string DataPath => root;

    public bool IsUnlocked
    {
        get
        {
            lock (gate)
                return key != null;
        }
    }

    public Site Hydrate(Site site)
    {
        lock (gate)
        {
            var saved = Read<List<Site>>("sites.json", []).Find(s => s.Id == site.Id && s.Host == site.Host && s.Username == site.Username && s.Port == site.Port && s.Protocol == site.Protocol);
            if (saved == null)
                return site;
            return SiteSecrets.Merge(site, saved, value => key == null ? throw new InvalidOperationException("저장된 비밀번호를 사용하려면 Vault를 잠금 해제하세요.") : Decrypt(key, value));
        }
    }

    public void Save(Site site)
    {
        lock (gate)
        {
            var sites = Read<List<Site>>("sites.json", []);
            if (site.SavePassword && key == null)
                throw new InvalidOperationException("비밀번호 저장 전에 Vault를 잠금 해제하세요.");
            var hydrated = site.SavePassword ? Hydrate(site) : site;
            hydrated.Validate();
            hydrated = hydrated with
            {
                EncryptFiles = hydrated.EncryptFiles || hydrated.EncryptionKey != null
            };
            sites.RemoveAll(s => s.Id == site.Id);
            var stored = SiteSecrets.Map(hydrated, value => site.SavePassword ? Encrypt(key!, value) : null);
            sites.Add(stored with { HasPassword = SiteSecrets.HasAny(stored) });
            Write("sites.json", sites);
            if (sites.Any(s => s.SavePassword && SiteSecrets.HasAny(s)))
            {
                if (key != null)
                    automaticUnlock = VaultAutoUnlock.Store(root, key);
            }
            else
            {
                VaultAutoUnlock.Remove(root);
                automaticUnlock = false;
            }
        }
    }

    public void Delete(string id)
    {
        lock (gate)
        {
            var sites = Read<List<Site>>("sites.json", []);
            sites.RemoveAll(s => s.Id == id);
            Write("sites.json", sites);
            if (!sites.Any(s => s.SavePassword && SiteSecrets.HasAny(s)))
            {
                VaultAutoUnlock.Remove(root);
                automaticUnlock = false;
            }
        }
    }

    public int ImportSites(IEnumerable<Site> source)
    {
        lock (gate)
        {
            // 전체를 검증한 뒤 한 번에 저장하고 새 ID를 부여해 기존 사이트와 Vault 비밀을 보존합니다.
            var incoming = source.Select(s => SiteArchive.Metadata(s) with { Id = Guid.NewGuid().ToString("N") }).ToArray();
            foreach (var site in incoming)
                site.Validate(requireSecrets: false);
            if (incoming.Length == 0)
                return 0;
            var sites = Read<List<Site>>("sites.json", []);
            sites.AddRange(incoming);
            Write("sites.json", sites);
            return incoming.Length;
        }
    }

    public Preferences Preferences()
    {
        lock (gate)
            return Read("preferences.json", new Preferences());
    }

    public void Preferences(Preferences value)
    {
        lock (gate)
            Write("preferences.json", value);
    }

    internal T ReadState<T>(string name, T fallback)
    {
        lock (gate)
            return Read(name, fallback);
    }

    internal void WriteState<T>(string name, T value)
    {
        lock (gate)
            Write(name, value);
    }

    public void Dispose()
    {
        lock (gate)
            ClearKey();
    }
}
