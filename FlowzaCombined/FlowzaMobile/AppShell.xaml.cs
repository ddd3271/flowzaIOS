namespace FlowzaMobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(Pages.PlaylistPage), typeof(Pages.PlaylistPage));
    }
}
