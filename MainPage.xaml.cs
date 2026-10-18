using System;
using ClipVault.ViewModels;
using ClipVault.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace ClipVault;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private bool _isInitialized;

    private CancellationTokenSource? _resizeCts;

    public MainPage(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGridView) && _viewModel.IsGridView)
            {
                UpdateGridSpanSafe();
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_isInitialized)
        {
            _isInitialized = true;
            await _viewModel.InitializeAsync();
        }
        else
        {
            _viewModel.UpdateStatusSubtitle();
        }
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

    private void UpdateGridSpanSafe()
    {
        try
        {
            if (Width <= 0 || !_viewModel.IsGridView) return;

            double width = Width;
            int targetSpan = width switch
            {
                < 520 => 1,
                < 820 => 2,
                < 1150 => 3,
                _ => 4
            };

            targetSpan = Math.Clamp(targetSpan, 1, 4);

            if (_viewModel.GridSpan != targetSpan)
            {
                _viewModel.GridSpan = targetSpan;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao calcular GridSpan seguro: {ex.Message}");
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        try
        {
            if (Width <= 0 || !_isInitialized || !_viewModel.IsGridView) return;

            double width = Width;
            int targetSpan = width switch
            {
                < 520 => 1,
                < 820 => 2,
                < 1150 => 3,
                _ => 4
            };

            targetSpan = Math.Clamp(targetSpan, 1, 4);

            if (_viewModel.GridSpan == targetSpan) return;

            _resizeCts?.Cancel();
            _resizeCts?.Dispose();
            var cts = new CancellationTokenSource();
            _resizeCts = cts;

            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () =>
            {
                if (cts.IsCancellationRequested) return;

                try
                {
                    if (_viewModel.GridSpan != targetSpan && _viewModel.IsGridView)
                    {
                        _viewModel.GridSpan = targetSpan;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro ao atualizar GridSpan: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnPageSizeChanged erro: {ex.Message}");
        }
    }
}
