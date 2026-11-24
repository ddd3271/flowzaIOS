namespace FlowzaMobile.Models;

/// <summary>
/// Trimmed-down version of the desktop AppSettings (Settings.cs). Everything tied to the built-in
/// audio engine (volume, EQ, fade, speed, output device) is gone on purpose: on Android, Flowza hands
/// playback off to the real Spotify app (see SpotifyService.OpenInSpotifyAsync), so those settings
/// have nothing to act on here. What's left is the stuff that still means something on a phone.
/// </summary>
public class AppSettings
{
    public string Theme { get; set; } = "glass";          // glass, or one of the swatch keys below
    public bool AmbientGlow { get; set; } = true;
    public bool ShootingStars { get; set; }
    public bool CoverColors { get; set; } = true;          // glow takes the colour of the current cover
    public string DismissedAnnouncement { get; set; } = "";
}
