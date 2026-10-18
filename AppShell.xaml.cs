using ClipVault.Views;
using Microsoft.Maui.Controls;

namespace ClipVault;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
    }
}
