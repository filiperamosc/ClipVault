using System;
using ClipVault.ViewModels;
using Microsoft.Maui.Controls;

namespace ClipVault.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadFromConfig();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
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
}
