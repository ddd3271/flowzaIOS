using FlowzaMobile.Services;

namespace FlowzaMobile.Pages;

public partial class SettingsPage : ContentPage
{
    readonly SpotifyService spotify;
    readonly SettingsStore store;
    readonly AccountService accounts;

    public SettingsPage(SpotifyService spotify, SettingsStore store, AccountService accounts)
    {
        InitializeComponent();
        this.spotify = spotify;
        this.store = store;
        this.accounts = accounts;

        GlowSwitch.IsToggled = store.Current.AmbientGlow;
        StarsSwitch.IsToggled = store.Current.ShootingStars;
        CoverSwitch.IsToggled = store.Current.CoverColors;
        ClientIdEntry.Text = spotify.ClientId ?? "";
        FooterLabel.Text = $"🎧 Flowza v{AppInfo.Current.VersionString} · Made by Crexx";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var acc = accounts.Current;
        AccountNameLabel.Text = acc == null ? "Not logged in" : acc.Username;
        AccountSubLabel.Text = acc == null ? "" : $"Discord: {acc.DiscordName}";
        DiscordAvatarImage.Source = string.IsNullOrEmpty(acc?.Picture) ? null : ImageSource.FromUri(new Uri(acc!.Picture));
        LogoutButton.IsEnabled = acc != null;

        bool signedIn = await spotify.IsSignedInAsync();
        SpotifyStatusLabel.Text = signedIn ? $"Connected as {spotify.DisplayName ?? "you"}" : "Not connected";
        AvatarImage.Source = string.IsNullOrEmpty(spotify.AvatarUrl) ? null : ImageSource.FromUri(new Uri(spotify.AvatarUrl));
        ConnectButton.IsVisible = !signedIn;
        SyncButton.IsEnabled = signedIn;
        SignOutButton.IsEnabled = signedIn;
    }

    void OnSettingToggled(object sender, ToggledEventArgs e)
    {
        store.Current.AmbientGlow = GlowSwitch.IsToggled;
        store.Current.ShootingStars = StarsSwitch.IsToggled;
        store.Current.CoverColors = CoverSwitch.IsToggled;
        store.Save();
    }

    void SaveClientId()
    {
        var id = (ClientIdEntry.Text ?? "").Trim();
        if (id.Length > 0) spotify.SetClientId(id);
    }

    void OnClientIdCompleted(object sender, EventArgs e) => SaveClientId();

    async void OnSaveClientIdClicked(object sender, EventArgs e)
    {
        SaveClientId();
        await DisplayAlert("Spotify", "Client ID saved. Add flowza://callback as a Redirect URI in your Spotify app, then tap Connect.", "OK");
    }

    async void OnConnectClicked(object sender, EventArgs e)
    {
        SaveClientId();
        try
        {
            await spotify.LoginAsync();
            OnAppearing();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Spotify", ex.Message, "OK");
        }
    }

    async void OnSyncClicked(object sender, EventArgs e)
    {
        try
        {
            var list = await spotify.MyPlaylistsAsync();
            await DisplayAlert("Sync", $"Synced {list.Count} playlist(s).", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sync", ex.Message, "OK");
        }
    }

    async void OnSignOutClicked(object sender, EventArgs e)
    {
        await spotify.SignOutAsync();
        OnAppearing();
    }

    async void OnLogoutClicked(object sender, EventArgs e)
    {
        accounts.SignOut();
        await Shell.Current.GoToAsync("//login");
    }
}
