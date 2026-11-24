using System.Text.Json;
using FlowzaMobile.Models;

namespace FlowzaMobile.Services;

/// <summary>
/// Read-only port of Admin.cs's AdminApi.FetchAsync. The phone app checks the exact same public
/// control.json your desktop app already reads, so a maintenance window or announcement you publish
/// from the desktop Owner/Founder panel shows up here too. Editing (the panel itself, the GitHub write
/// token) stays desktop-only on purpose - a write token has no reason to live on a phone.
/// </summary>
public class AdminService
{
    static readonly HttpClient Http = new();

    // same repo the desktop app's AdminApi.DefaultRepo points at
    public const string Repo = "ddd3271/spotify-reborn-control";

    public async Task<AdminData?> FetchAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{Repo}/contents/control.json";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("FlowzaMobile");
            req.Headers.Accept.ParseAdd("application/vnd.github.raw");
            var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            var b64 = (await res.Content.ReadAsStringAsync());
            // GitHub's "raw" media type returns the file directly; fall back to decoding the JSON+base64
            // form in case a proxy strips the Accept header.
            string json;
            try
            {
                using var probe = JsonDocument.Parse(b64);
                var content = probe.RootElement.GetProperty("content").GetString() ?? "";
                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
            }
            catch { json = b64; }
            return JsonSerializer.Deserialize<AdminData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }   // never block the app on a failed maintenance check
    }
}
