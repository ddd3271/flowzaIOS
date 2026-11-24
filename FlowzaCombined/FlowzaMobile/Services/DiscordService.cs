using System.Net.Http.Headers;
using System.Text.Json;

namespace FlowzaMobile.Services;

/// <summary>
/// Discord authorization on Android - the same check the desktop app does (DiscordAuthorizeAsync in MainWindow.xaml.cs):
/// the person signs in with Discord in the browser, must be a member of the Flowza server and have the Flowza role.
/// The login screen of Discord redirects to "flowza://callback", which WebAuthenticationCallbackActivity hands back to the app.
/// One-time setup: add  flowza://callback  as a Redirect URI in the Discord developer portal
/// (Applications > your app > OAuth2 > Redirects), next to the desktop one (http://127.0.0.1:5544/callback).
/// </summary>
public class DiscordService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // same values as Config.cs / Providers.cs on the desktop (not secrets)
    public const string ClientId = "1566133865880354976";
    public const string RoleId = "1566131666777542867";      // members need this role on the Discord server
    public const string InviteUrl = "https://discord.gg/2FHffDYxsE";
    const string RedirectUri = "flowza://callback";

    static readonly Dictionary<string, string> RoleNames = new()
    {
        ["1566131665766453511"] = "Owner",
        ["1566131666018369666"] = "Friend",
        ["1566131666777542867"] = "Flowza",
        ["1570295570600558742"] = "Sneak Peek"
    };

    /// <summary>Throws an Exception with a message that can be shown to the user as it is.</summary>
    public async Task<DiscordProfile> AuthorizeAsync()
    {
        var authUrl = "https://discord.com/oauth2/authorize" +
            $"?client_id={ClientId}&response_type=token&scope={Uri.EscapeDataString("identify guilds.members.read")}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(authUrl),
                CallbackUrl = new Uri(RedirectUri),
            });
        }
        catch (TaskCanceledException) { throw new Exception("The Discord login was cancelled."); }

        if (!result.Properties.TryGetValue("access_token", out var token) || string.IsNullOrEmpty(token))
            throw new Exception("Discord didn't send back a token. Check that flowza://callback is added as a Redirect URI in the Discord developer portal.");

        try
        {
            using var me = await GetAsync("https://discord.com/api/v10/users/@me", token);
            var r = me.RootElement;
            string uid = r.GetProperty("id").GetString() ?? "";
            string? display = r.TryGetProperty("global_name", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
            string name = display ?? r.GetProperty("username").GetString() ?? "Discord user";
            string avatar = r.TryGetProperty("avatar", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
            string pic = avatar.Length > 0
                ? $"https://cdn.discordapp.com/avatars/{uid}/{avatar}.png?size=256"
                : $"https://cdn.discordapp.com/embed/avatars/{(ulong.TryParse(uid, out var n) ? (n >> 22) % 6 : 0)}.png";

            // the person must be a member of our Discord server (the server is found from the invite link)
            string? guildId = await InviteGuildIdAsync();
            if (string.IsNullOrEmpty(guildId))
                throw new Exception("Couldn't check the Discord server right now. Check your internet and try again.");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v10/users/@me/guilds/{guildId}/member");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var mres = await Http.SendAsync(req);
            if (!mres.IsSuccessStatusCode)
            {
                try { await Launcher.Default.OpenAsync(new Uri(InviteUrl)); } catch { }
                throw new Exception("You are not on our Discord server yet. Join it, then try again.");
            }

            var roleList = new List<string>();
            using (var mdoc = JsonDocument.Parse(await mres.Content.ReadAsStringAsync()))
                if (mdoc.RootElement.TryGetProperty("roles", out var roles))
                    foreach (var x in roles.EnumerateArray()) if (x.GetString() is { } rid) roleList.Add(rid);
            if (!roleList.Contains(RoleId))
                throw new Exception("You are on the server, but you need the \"Flowza\" role to use the app. Ask on the Discord server to get it, then try again.");

            // role names come from the list above (a bot token must never live on a phone, so no extra role lookup here)
            var labels = roleList.Select(rid => (RoleNames.TryGetValue(rid, out var rn) ? rn : "Role …" + (rid.Length > 4 ? rid[^4..] : rid)) + "~99AAB5");
            return new DiscordProfile(uid, name, pic, string.Join(",", roleList), string.Join("|", labels));
        }
        catch (HttpRequestException) { throw new Exception("Couldn't reach Discord. Check your internet and try again."); }
    }

    static async Task<JsonDocument> GetAsync(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    static async Task<string?> InviteGuildIdAsync()
    {
        try
        {
            var code = InviteUrl[(InviteUrl.LastIndexOf('/') + 1)..];
            using var d = JsonDocument.Parse(await Http.GetStringAsync("https://discord.com/api/v10/invites/" + code));
            return d.RootElement.GetProperty("guild").GetProperty("id").GetString();
        }
        catch { return null; }
    }
}
