using System;
using ClipVault.ViewModels;
using ClipVault.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace ClipVault;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public MainPage(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        var settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();
        await Navigation.PushAsync(settingsPage);
    }

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        var aboutPage = _serviceProvider.GetRequiredService<AboutPage>();
        await Navigation.PushAsync(aboutPage);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width > 0)
        {
            double width = Width;
            int span = width switch
            {
                < 520 => 1,
                < 820 => 2,
                < 1150 => 3,
                _ => 4
            };

            if (_viewModel.GridSpan != span)
            {
                _viewModel.GridSpan = span;
            }
        }
    }
}
