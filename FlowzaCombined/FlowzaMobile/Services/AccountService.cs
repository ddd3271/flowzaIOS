using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FlowzaMobile.Services;

/// <summary>What Discord told us about the person (filled in by DiscordService.AuthorizeAsync).</summary>
public record DiscordProfile(string Id, string Name, string Picture, string Roles, string RoleLabels);

/// <summary>One Flowza account on this phone: username + salted password hash + the Discord account it is tied to.</summary>
public class AccountRecord
{
    public string Username { get; set; } = "";
    public string Salt { get; set; } = "";      // base64
    public string Hash { get; set; } = "";      // base64, PBKDF2-SHA256
    public int Iterations { get; set; } = 150_000;
    public string DiscordId { get; set; } = "";
    public string DiscordName { get; set; } = "";
    public string Picture { get; set; } = "";
    public string Roles { get; set; } = "";
    public string RoleLabels { get; set; } = "";
    public DateTime VerifiedUtc { get; set; }   // last time Discord confirmed this person (server member + role)
}

/// <summary>
/// Same idea as the desktop AccountStore (Accounts.cs): local username + password accounts, the password is never
/// stored (PBKDF2 hash only), registering needs the Discord authorization, a login re-checks it every 14 days.
/// The accounts live on this phone (there is no Flowza server); on another device you register once more with the
/// same Discord account.
/// </summary>
public class AccountService
{
    public const int ReverifyDays = 14;
    const int Iterations = 150_000;
    const string SessionKey = "session_user";
    const string RememberKey = "remember_me";
    const string LastUserKey = "last_username";

    static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "accounts.json");
    static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);

    readonly object gate = new();
    readonly Dictionary<string, (int Fails, DateTime Until)> attempts = new(StringComparer.OrdinalIgnoreCase);
    List<AccountRecord> accounts;

    /// <summary>The account that is logged in right now (kept across app starts until you log out).</summary>
    public AccountRecord? Current { get; private set; }

    public AccountService()
    {
        try { accounts = JsonSerializer.Deserialize<List<AccountRecord>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { accounts = new(); }
        // "Remember me" off: the login is not kept after closing the app
        var name = Preferences.Default.Get(SessionKey, "");
        if (!RememberMe) { Preferences.Default.Remove(SessionKey); name = ""; }
        if (name.Length > 0) Current = Find(name);
    }

    /// <summary>"Remember me": stay logged in and fill in the username next time. The password itself is never saved.</summary>
    public bool RememberMe => Preferences.Default.Get(RememberKey, true);
    public string LastUsername => RememberMe ? Preferences.Default.Get(LastUserKey, "") : "";
    public void SetRemember(bool on, string username)
    {
        Preferences.Default.Set(RememberKey, on);
        if (on) Preferences.Default.Set(LastUserKey, username); else Preferences.Default.Remove(LastUserKey);
    }

    void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(accounts)); }
        catch { }
    }

    public static string? CheckUsername(string username)
    {
        var u = (username ?? "").Trim();
        if (u.Length < 3 || u.Length > 20) return "The username needs 3 to 20 characters.";
        foreach (var c in u)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')) return "The username may only contain letters, numbers and _ . -";
        return null;
    }

    public static string? CheckPassword(string password)
    {
        password ??= "";
        if (password.Length < 8) return "The password needs at least 8 characters.";
        if (password.Length > 100) return "The password is too long (max 100 characters).";
        return null;
    }

    static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    AccountRecord? Find(string username)
    {
        lock (gate) return accounts.FirstOrDefault(a => a.Username.Equals((username ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool NeedsReverify(AccountRecord r) => (DateTime.UtcNow - r.VerifiedUtc).TotalDays > ReverifyDays;

    /// <summary>
    /// Creates the account after Discord confirmed who the person is and logs it in. If this Discord account already has a
    /// Flowza account here, that account gets the new username/password (the "forgot password" way: Discord proves it is you).
    /// Returns an error text, or null when it worked.
    /// </summary>
    public async Task<string?> RegisterAsync(string username, string password, DiscordProfile d)
    {
        username = (username ?? "").Trim();
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = await Task.Run(() => HashPassword(password, salt, Iterations));
        lock (gate)
        {
            var byName = accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            var byDiscord = accounts.FirstOrDefault(a => a.DiscordId == d.Id);
            if (byName != null && byName.DiscordId != d.Id) return "That username is already taken. Pick another one.";
            var rec = byName ?? byDiscord;
            if (rec == null) { rec = new AccountRecord(); accounts.Add(rec); }
            rec.Username = username;
            rec.Salt = Convert.ToBase64String(salt);
            rec.Hash = Convert.ToBase64String(hash);
            rec.Iterations = Iterations;
            Apply(rec, d);
            Save();
            attempts.Remove(username);
            SetSession(rec);
        }
        return null;
    }

    /// <summary>Checks username and password and logs in. Too many wrong tries lock that username for a short while.</summary>
    public async Task<(AccountRecord? Record, string? Error)> LoginAsync(string username, string password)
    {
        username = (username ?? "").Trim();
        AccountRecord? rec;
        lock (gate)
        {
            if (attempts.TryGetValue(username, out var a) && a.Until > DateTime.UtcNow)
                return (null, "Too many wrong tries. Wait " + (int)Math.Ceiling((a.Until - DateTime.UtcNow).TotalSeconds) + " seconds.");
            rec = accounts.FirstOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }
        // hash even when the user does not exist, so the answer takes the same time
        var salt = rec != null ? Convert.FromBase64String(rec.Salt) : DummySalt;
        int iter = rec?.Iterations ?? Iterations;
        var h = await Task.Run(() => HashPassword(password ?? "", salt, iter));
        bool ok = rec != null && CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(rec.Hash));
        lock (gate)
        {
            if (ok) attempts.Remove(username);
            else
            {
                attempts.TryGetValue(username, out var a);
                int fails = a.Fails + 1;
                attempts[username] = fails >= 5 ? (0, DateTime.UtcNow.AddSeconds(30)) : (fails, DateTime.MinValue);
            }
            if (ok) SetSession(rec!);
        }
        return ok ? (rec, null) : (null, "Wrong username or password.");
    }

    /// <summary>After a fresh Discord check (14-day re-verification).</summary>
    public void UpdateDiscord(AccountRecord rec, DiscordProfile d)
    {
        lock (gate) { Apply(rec, d); Save(); }
    }

    public void SignOut()
    {
        Current = null;
        Preferences.Default.Remove(SessionKey);
    }

    void SetSession(AccountRecord rec)
    {
        Current = rec;
        Preferences.Default.Set(SessionKey, rec.Username);
    }

    static void Apply(AccountRecord rec, DiscordProfile d)
    {
        rec.DiscordId = d.Id;
        rec.DiscordName = d.Name;
        rec.Picture = d.Picture;
        rec.Roles = d.Roles;
        rec.RoleLabels = d.RoleLabels;
        rec.VerifiedUtc = DateTime.UtcNow;
    }
}
