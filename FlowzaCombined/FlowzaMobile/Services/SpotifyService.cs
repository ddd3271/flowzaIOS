using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowzaMobile.Models;
using Microsoft.Maui.Authentication;

namespace FlowzaMobile.Services;

/// <summary>
/// Android port of the desktop Spotify provider (Providers.cs). Same PKCE flow, same Web API calls,
/// same 429 retry logic - what's different is plumbing that's genuinely platform-specific:
///   - login:   desktop opens the system browser and catches the redirect with a local HttpListener on
///              127.0.0.1; Android has no loopback browser redirect, so this uses MAUI's WebAuthenticator
///              with a custom "flowza://callback" scheme instead (see Platforms/Android for the intent filter).
///   - tokens:  desktop encrypts the refresh token with Windows DPAPI into a file; Android uses
///              SecureStorage, which is backed by the Android Keystore.
///   - playback: desktop either lets the real Spotify desktop app play (and controls it over the Web API)
///              or, when that's not possible, falls back to a YouTube-matched local engine. Neither exists
///              here - Android hands tracks to the real, installed Spotify app via OpenInSpotifyAsync
///              (Spotify URI deep link). That's a deliberate scope cut: a true in-app player on Android
///              needs Spotify's App Remote SDK, which ships as a proprietary Android .aar with no official
///              .NET/MAUI binding - wiring that up means creating a MAUI Android Binding Library project
///              around the .aar you get from Spotify's dashboard after they approve App Remote for your
///              app. That's a follow-up once you have that approval, not something that can be faked here.
/// </summary>
public class SpotifyService
{
    static readonly HttpClient Http = new();

    // Same client id story as desktop: not a secret (PKCE), just register your own app at
    // https://developer.spotify.com/dashboard and add "flowza://callback" as a Redirect URI there.
    public string? ClientId { get; set; } = Preferences.Default.Get<string?>("spotify_client_id", null);

    const string RedirectUri = "flowza://callback";
    const string Scopes = "user-read-email user-read-private playlist-read-private playlist-read-collaborative";
    const string TokenKey = "spotify_refresh_token";

    string? access;
    DateTime expiresUtc;

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);
    public string? DisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }

    public void SetClientId(string id)
    {
        ClientId = id;
        Preferences.Default.Set("spotify_client_id", id);
    }

    public async Task<bool> IsSignedInAsync() => !string.IsNullOrEmpty(await SecureStorage.Default.GetAsync(TokenKey));

    // ---- sign-in (PKCE, same shape as Providers.cs LoginAsync/ExchangeAsync) ----
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task LoginAsync()
    {
        if (!Enabled) throw new Exception("No Spotify Client ID set yet. Open Settings > Accounts and paste one in.");

        var verifier = B64(RandomNumberGenerator.GetBytes(64));
        var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64(RandomNumberGenerator.GetBytes(16));

        var authUrl = "https://accounts.spotify.com/authorize" +
            $"?client_id={ClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scopes)}&code_challenge_method=S256&code_challenge={challenge}&state={state}";

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(authUrl),
                CallbackUrl = new Uri(RedirectUri),
                PrefersEphemeralWebBrowserSession = false,
            });
        }
        catch (TaskCanceledException) { throw new Exception("Spotify sign-in was cancelled."); }

        if (result.Properties.TryGetValue("state", out var gotState) && gotState != state)
            throw new Exception("Spotify sign-in state didn't match - try again.");
        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new Exception("Spotify didn't send back a code.");

        await ExchangeAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId!,
            ["code_verifier"] = verifier,
        });

        await RefreshProfileAsync();
    }

    public async Task SignOutAsync()
    {
        SecureStorage.Default.Remove(TokenKey);
        access = null;
        DisplayName = null;
        AvatarUrl = null;
    }

    async Task ExchangeAsync(Dictionary<string, string> form)
    {
        var res = await Http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode) throw new Exception("Spotify sign-in failed.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        access = r.GetProperty("access_token").GetString();
        expiresUtc = DateTime.UtcNow.AddSeconds(r.GetProperty("expires_in").GetInt32() - 60);
        if (r.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { } refresh)
            await SecureStorage.Default.SetAsync(TokenKey, refresh);
    }

    async Task<string> TokenAsync()
    {
        if (access != null && DateTime.UtcNow < expiresUtc) return access;
        var refresh = await SecureStorage.Default.GetAsync(TokenKey);
        if (refresh == null) { await LoginAsync(); return access!; }
        try { await ExchangeAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = ClientId! }); return access!; }
        catch { SecureStorage.Default.Remove(TokenKey); await LoginAsync(); return access!; }
    }

    // ---- API (ported 1:1 from Providers.cs, including the 429 retry fix) ----

    /// <summary>Same fix as the desktop SendAsync: on 429 ("Spotify is rate-limiting this app right now"),
    /// wait for Retry-After (or an exponential backoff if it's missing) and try again, up to 5 attempts,
    /// instead of failing the whole sync on the first hiccup.</summary>
    async Task<HttpResponseMessage> SendAsync(HttpMethod m, string url, object? body = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            var req = new HttpRequestMessage(m, "https://api.spotify.com/v1" + url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
            if (m != HttpMethod.Get)
                req.Content = body != null ? new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") : new StringContent("");
            var res = await Http.SendAsync(req);
            if (res.StatusCode != (HttpStatusCode)429 || attempt >= 4) return res;
            var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
            res.Dispose();
            await Task.Delay(wait);
        }
    }

    async Task RefreshProfileAsync()
    {
        try
        {
            var res = await SendAsync(HttpMethod.Get, "/me");
            if (!res.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            DisplayName = r.TryGetProperty("display_name", out var n) ? n.GetString() : null;
            if (r.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0)
                AvatarUrl = imgs[0].GetProperty("url").GetString();
        }
        catch { /* profile is cosmetic only, never block sign-in on it */ }
    }

    static Track ToTrack(JsonElement t)
    {
        var images = t.GetProperty("album").GetProperty("images");
        return new Track(t.GetProperty("id").GetString()!, t.GetProperty("name").GetString() ?? "",
            t.GetProperty("artists")[0].GetProperty("name").GetString() ?? "",
            images.GetArrayLength() > 0 ? images[0].GetProperty("url").GetString() : null,
            t.GetProperty("uri").GetString(), t.TryGetProperty("duration_ms", out var dm) ? dm.GetInt32() : 0);
    }

    public async Task<List<Track>> SearchAsync(string q)
    {
        var res = await SendAsync(HttpMethod.Get, $"/search?type=track&limit=20&q={Uri.EscapeDataString(q)}");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<Track>();
        foreach (var t in doc.RootElement.GetProperty("tracks").GetProperty("items").EnumerateArray())
            list.Add(ToTrack(t));
        return list;
    }

    public async Task<List<PlaylistInfo>> MyPlaylistsAsync()
    {
        var list = new List<PlaylistInfo>();
        string? next = "/me/playlists?limit=50";
        while (next != null && list.Count < 300)
        {
            var res = await SendAsync(HttpMethod.Get, next);
            if (!res.IsSuccessStatusCode) throw new Exception(res.StatusCode == (HttpStatusCode)429
                ? "Spotify is rate-limiting this app right now. It already retried a few times - wait a minute and pull to refresh again."
                : $"Spotify wouldn't list your playlists (error {(int)res.StatusCode}).");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                try
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    string pid = it.GetProperty("id").GetString() ?? "";
                    string name = it.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
                    string snap = it.TryGetProperty("snapshot_id", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString() ?? "" : "";
                    int count = it.TryGetProperty("tracks", out var tr) && tr.TryGetProperty("total", out var tc) ? tc.GetInt32() : 0;
                    if (pid.Length > 0 && name.Length > 0) list.Add(new PlaylistInfo(pid, name, snap, count));
                }
                catch { }
            }
            next = doc.RootElement.TryGetProperty("next", out var nx) && nx.ValueKind == JsonValueKind.String ? nx.GetString()!.Replace("https://api.spotify.com/v1", "") : null;
        }
        return list;
    }

    public async Task<List<Track>> PlaylistAsync(string id)
    {
        var list = new List<Track>();
        string? next = $"/playlists/{id}/items?limit=50";
        while (next != null && list.Count < 1000)
        {
            var res = await SendAsync(HttpMethod.Get, next);
            if (!res.IsSuccessStatusCode) throw new Exception($"Spotify wouldn't share that playlist with this app (error {(int)res.StatusCode}).");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                try { if ((it.TryGetProperty("item", out var t) || it.TryGetProperty("track", out t)) && t.ValueKind == JsonValueKind.Object) list.Add(ToTrack(t)); }
                catch { }
            }
            next = doc.RootElement.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Replace("https://api.spotify.com/v1", "") : null;
        }
        return list;
    }

    /// <summary>
    /// The actual "playback" on Android: hand the track to the real Spotify app via its URI scheme
    /// (spotify:track:ID). Falls back to the open.spotify.com web link if Spotify isn't installed.
    /// This is legitimate - it's the same kind of deep link Spotify's own share button produces -
    /// unlike the desktop app's YouTube-matching fallback, it never touches or copies any audio itself.
    /// </summary>
    public async Task OpenInSpotifyAsync(Track t)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri($"spotify:track:{t.Id}"));
        }
        catch
        {
            await Launcher.Default.OpenAsync(new Uri(t.OpenUri ?? $"https://open.spotify.com/track/{t.Id}"));
        }
    }
}
