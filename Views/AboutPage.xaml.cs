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
        await GoBackSafelyAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await GoBackSafelyAsync();
    }

    private async System.Threading.Tasks.Task GoBackSafelyAsync()
    {
        try
        {
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync();
            }
            else if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        catch
        {
            try { await Navigation.PopAsync(); } catch { }
        }
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
