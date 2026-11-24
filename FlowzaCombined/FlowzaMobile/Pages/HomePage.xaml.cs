using System.Collections.ObjectModel;
using FlowzaMobile.Models;
using FlowzaMobile.Services;

namespace FlowzaMobile.Pages;

public partial class HomePage : ContentPage
{
    readonly SpotifyService spotify;
    readonly AdminService admin;
    readonly ObservableCollection<PlaylistInfo> playlists = new();

    public HomePage(SpotifyService spotify, AdminService admin)
    {
        InitializeComponent();
        this.spotify = spotify;
        this.admin = admin;
        PlaylistList.ItemsSource = playlists;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UpdateConnectCardAsync();
        if (playlists.Count == 0 && await spotify.IsSignedInAsync()) await SyncAsync();

        var data = await admin.FetchAsync();
        if (data?.AnnouncementLive == true)
        {
            AnnouncementLabel.Text = (data.AnnouncementTitle.Length > 0 ? data.AnnouncementTitle + " - " : "") + data.Announcement;
            AnnouncementLabel.IsVisible = true;
        }
    }

    // not connected to Spotify yet: show the connect card (the Spotify Client ID is entered in Settings)
    async Task UpdateConnectCardAsync()
    {
        bool signedIn = await spotify.IsSignedInAsync();
        ConnectCard.IsVisible = !signedIn;
        if (signedIn) return;
        ConnectLabel.Text = spotify.Enabled
            ? "Flowza plays through your own Spotify account. Connect it to see your playlists."
            : "Flowza plays through your own Spotify account. Open the Settings tab and paste your Spotify Client ID first, then connect here.";
    }

    async void OnConnectClicked(object sender, EventArgs e)
    {
        if (!spotify.Enabled)
        {
            await DisplayAlert("Spotify", "Open the Settings tab and paste your Spotify Client ID first.", "OK");
            return;
        }
        ConnectButton.IsEnabled = false;
        try
        {
            await spotify.LoginAsync();
            await UpdateConnectCardAsync();
            await SyncAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Spotify", ex.Message, "OK");
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    async void OnRefreshing(object sender, EventArgs e)
    {
        if (!await spotify.IsSignedInAsync()) { Refresher.IsRefreshing = false; await UpdateConnectCardAsync(); return; }
        await SyncAsync();
        Refresher.IsRefreshing = false;
    }

    async Task SyncAsync()
    {
        try
        {
            var fresh = await spotify.MyPlaylistsAsync();
            playlists.Clear();
            foreach (var p in fresh) playlists.Add(p);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sync", ex.Message, "OK");
        }
    }

    async void OnPlaylistSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaylistInfo p) return;
        PlaylistList.SelectedItem = null;
        await Shell.Current.GoToAsync(nameof(PlaylistPage), new Dictionary<string, object> { ["Playlist"] = p });
    }
}
