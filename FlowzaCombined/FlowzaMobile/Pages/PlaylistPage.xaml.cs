using System.Collections.ObjectModel;
using FlowzaMobile.Models;
using FlowzaMobile.Services;

namespace FlowzaMobile.Pages;

[QueryProperty(nameof(Playlist), "Playlist")]
public partial class PlaylistPage : ContentPage
{
    readonly SpotifyService spotify;
    readonly ObservableCollection<Track> tracks = new();
    PlaylistInfo? playlist;

    public PlaylistInfo? Playlist
    {
        get => playlist;
        set { playlist = value; Title = value?.Name ?? "Playlist"; _ = LoadAsync(); }
    }

    public PlaylistPage(SpotifyService spotify)
    {
        InitializeComponent();
        this.spotify = spotify;
        TrackList.ItemsSource = tracks;
    }

    async Task LoadAsync()
    {
        if (playlist == null) return;
        try
        {
            var fresh = await spotify.PlaylistAsync(playlist.Id);
            tracks.Clear();
            foreach (var t in fresh) tracks.Add(t);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Playlist", ex.Message, "OK");
        }
    }

    async void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Track t) return;
        TrackList.SelectedItem = null;
        await spotify.OpenInSpotifyAsync(t);
    }
}
