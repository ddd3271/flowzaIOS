using FlowzaMobile.Services;

namespace FlowzaMobile.Pages;

public partial class LoginPage : ContentPage
{
    readonly AccountService accounts;
    readonly DiscordService discord;
    readonly AdminService admin;
    bool registerMode, busy, maintenance;

    public LoginPage(AccountService accounts, DiscordService discord, AdminService admin)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.discord = discord;
        this.admin = admin;
        VersionLabel.Text = $"Mobile · v{AppInfo.Current.VersionString}";
        RememberCheck.IsChecked = accounts.RememberMe;
        UsernameEntry.Text = accounts.LastUsername;
        SetMode(false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // same maintenance-banner idea as the desktop app's admin check, just read-only here
        var data = await admin.FetchAsync();
        maintenance = data?.Maintenance == true;
        if (maintenance)
        {
            MaintenanceBanner.Text = string.IsNullOrWhiteSpace(data!.MaintenanceText)
                ? "Flowza is down for maintenance right now - try again shortly."
                : data.MaintenanceText;
            MaintenanceBanner.IsVisible = true;
        }
        SubmitButton.IsEnabled = !maintenance;

        // already logged in on this phone (and not banned): straight into the app
        if (!maintenance && accounts.Current != null)
        {
            if (await IsBannedAsync(accounts.Current.DiscordId, data))
            {
                accounts.SignOut();
                ShowError("You are banned from Flowza.");
                return;
            }
            await Shell.Current.GoToAsync("//home");
        }
    }

    async Task<bool> IsBannedAsync(string discordId, FlowzaMobile.Models.AdminData? data = null)
    {
        data ??= await admin.FetchAsync();
        return data?.Banned.Any(b => b.Id == discordId && b.IsActive) == true;
    }

    void SetMode(bool register)
    {
        registerMode = register;
        LoginTab.Style = (Style)Application.Current!.Resources[register ? "GhostButton" : "PillButton"];
        RegisterTab.Style = (Style)Application.Current!.Resources[register ? "PillButton" : "GhostButton"];
        LoginTab.HeightRequest = RegisterTab.HeightRequest = 44;
        ConfirmEntry.IsVisible = register;
        SubmitButton.Text = register ? "Create account & authorize with Discord" : "Log in";
        HintLabel.Text = register
            ? "Register once: pick a username and password, then authorize Flowza with Discord (you must be on our server and have the Flowza role). Already registered with this Discord? Registering again sets a new password."
            : "Log in with your Flowza username and password.";
        ErrorLabel.IsVisible = false;
    }

    void OnLoginTab(object sender, EventArgs e) => SetMode(false);
    void OnRegisterTab(object sender, EventArgs e) => SetMode(true);
    void OnEntryCompleted(object sender, EventArgs e) => OnSubmitClicked(sender, e);

    void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    void SetBusy(bool on)
    {
        busy = on;
        Spinner.IsVisible = Spinner.IsRunning = on;
        SubmitButton.IsEnabled = !on && !maintenance;
        LoginTab.IsEnabled = RegisterTab.IsEnabled = !on;
    }

    async void OnSubmitClicked(object sender, EventArgs e)
    {
        if (busy) return;
        ErrorLabel.IsVisible = false;
        string user = (UsernameEntry.Text ?? "").Trim(), pass = PasswordEntry.Text ?? "";
        if (user.Length == 0 || pass.Length == 0) { ShowError("Enter a username and a password."); return; }
        if (registerMode && pass != (ConfirmEntry.Text ?? "")) { ShowError("The two passwords don't match."); return; }

        SetBusy(true);
        try
        {
            string? error = registerMode ? await RegisterAsync(user, pass) : await LoginAsync(user, pass);
            if (error != null) { ShowError(error); return; }
            accounts.SetRemember(RememberCheck.IsChecked, accounts.Current?.Username ?? user);
            PasswordEntry.Text = ConfirmEntry.Text = "";
            await Shell.Current.GoToAsync("//home");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    async Task<string?> RegisterAsync(string user, string pass)
    {
        var bad = AccountService.CheckUsername(user) ?? AccountService.CheckPassword(pass);
        if (bad != null) return bad;

        // step 1: Discord (the Flowza app / bot) confirms who you are: server member + Flowza role
        var profile = await discord.AuthorizeAsync();
        if (await IsBannedAsync(profile.Id)) return "You are banned from Flowza.";

        // step 2: create the account (username + password), tied to that Discord account
        return await accounts.RegisterAsync(user, pass, profile);
    }

    async Task<string?> LoginAsync(string user, string pass)
    {
        var (rec, error) = await accounts.LoginAsync(user, pass);
        if (rec == null) return error;

        if (AccountService.NeedsReverify(rec))
        {
            // the Discord confirmation is older than 14 days: ask Discord again (role taken away? left the server?)
            DiscordProfile profile;
            try { profile = await discord.AuthorizeAsync(); }
            catch { accounts.SignOut(); throw; }
            if (profile.Id != rec.DiscordId) { accounts.SignOut(); return "That Discord account is not the one this Flowza account belongs to."; }
            accounts.UpdateDiscord(rec, profile);
        }
        else if (!rec.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(DiscordService.RoleId))
        {
            accounts.SignOut();
            return "This account no longer has the Flowza role. Register again to check with Discord.";
        }

        if (await IsBannedAsync(rec.DiscordId))
        {
            accounts.SignOut();
            return "You are banned from Flowza.";
        }
        return null;
    }
}
