using System;
using Microsoft.Maui.Controls;

namespace ClipVault.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnRepositoryUrlTapped(object sender, EventArgs e)
    {
        try
        {
            await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new Uri("https://github.com/filiperamosc/ClipVault"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao abrir link do repositório: {ex.Message}");
        }
    }
}
