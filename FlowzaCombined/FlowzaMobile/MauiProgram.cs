using Microsoft.Extensions.DependencyInjection;

namespace FlowzaMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // drop a .ttf into Resources/Fonts and register it here if you want a custom face;
                // the desktop app just uses Segoe UI, Android falls back to its default sans
            });

        builder.Services.AddSingleton<Services.SpotifyService>();
        builder.Services.AddSingleton<Services.AdminService>();
        builder.Services.AddSingleton<Services.SettingsStore>();
        builder.Services.AddSingleton<Services.AccountService>();
        builder.Services.AddSingleton<Services.DiscordService>();

        builder.Services.AddTransient<Pages.LoginPage>();
        builder.Services.AddTransient<Pages.HomePage>();
        builder.Services.AddTransient<Pages.PlaylistPage>();
        builder.Services.AddTransient<Pages.SettingsPage>();

        return builder.Build();
    }
}
