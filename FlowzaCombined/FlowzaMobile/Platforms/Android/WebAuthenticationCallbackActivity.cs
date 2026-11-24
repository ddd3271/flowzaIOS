using Android.App;
using Android.Content.PM;
using Android.Content;

namespace FlowzaMobile;

/// <summary>
/// This is the Android-specific piece that replaces the desktop app's local HttpListener on
/// 127.0.0.1:5543. Spotify's auth page redirects the device browser to "flowza://callback?code=...",
/// and this intent filter is what makes Android hand that link back to the app instead of trying to
/// open it as a website. MAUI's WebAuthenticator (called from SpotifyService.LoginAsync) then picks
/// the code up from here automatically - nothing else needs to reference this class directly.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "flowza", DataHost = "callback")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
