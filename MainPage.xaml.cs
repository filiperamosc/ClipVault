using System;
using System.ComponentModel;
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

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGridView))
        {
            UpdateLayout(_viewModel.IsGridView);
        }
    }

    private void UpdateLayout(bool isGrid)
    {
        ItemsCollectionView.ItemsLayout = isGrid
            ? new GridItemsLayout(3, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 6, VerticalItemSpacing = 6 }
            : new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 4 };
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
}
