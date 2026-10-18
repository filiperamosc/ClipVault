using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipVault.Models;
using ClipVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using ClipVault.Views;

namespace ClipVault.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly IClipboardService _clipboardService;
    private readonly ITrayService _trayService;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;

    [ObservableProperty]
    private string _selectedFilter = "Todos";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isMonitoringPaused;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _statusSubtitle = string.Empty;

    [ObservableProperty]
    private bool _isGridView;

    [ObservableProperty]
    private bool _isListView = true;

    [ObservableProperty]
    private int _gridSpan = 3;

    // Segoe Fluent Icons: E8FD = BulletedList, F0E2 = GridView
    public string ViewModeIcon => IsGridView ? "\uE8FD" : "\uF0E2";
    public string ViewModeTooltip => IsGridView ? "Alternar para Modo Lista" : "Alternar para Modo Grade";

    public string FilterTodosBg => SelectedFilter == "Todos" ? "#6366F1" : "#1C1C21";
    public string FilterTodosFg => SelectedFilter == "Todos" ? "#FFFFFF" : "#A1A1AA";

    public string FilterTextosBg => SelectedFilter == "Textos" ? "#6366F1" : "#1C1C21";
    public string FilterTextosFg => SelectedFilter == "Textos" ? "#FFFFFF" : "#A1A1AA";

    public string FilterImagensBg => SelectedFilter == "Imagens" ? "#6366F1" : "#1C1C21";
    public string FilterImagensFg => SelectedFilter == "Imagens" ? "#FFFFFF" : "#A1A1AA";

    public string FilterFixadosBg => SelectedFilter == "Fixados" ? "#6366F1" : "#1C1C21";
    public string FilterFixadosFg => SelectedFilter == "Fixados" ? "#FFFFFF" : "#F59E0B";

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

    public MainViewModel(
        IDatabaseService databaseService,
        IClipboardService clipboardService,
        ITrayService trayService,
        ISettingsService settingsService,
        IStorageService storageService)
    {
        _databaseService = databaseService;
        _clipboardService = clipboardService;
        _trayService = trayService;
        _settingsService = settingsService;
        _storageService = storageService;

        _clipboardService.ItemCaptured += OnItemCaptured;
        _clipboardService.ItemUpdated += OnItemUpdated;

        _trayService.RequestClearHistory += () => MainThread.BeginInvokeOnMainThread(async () => await ClearHistoryAsync());
        _trayService.RequestToggleMonitoring += () => MainThread.BeginInvokeOnMainThread(async () => await ToggleMonitoringAsync());
    }

    private void OnItemCaptured(object? sender, ClipboardItem item)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (MatchesFilter(item))
            {
                var vm = new ClipboardItemViewModel(item);
                int insertIndex = 0;
                while (insertIndex < Items.Count && Items[insertIndex].IsPinned)
                {
                    insertIndex++;
                }
                Items.Insert(insertIndex, vm);
            }
            UpdateStatusSubtitle();
        });
    }

    private void OnItemUpdated(object? sender, ClipboardItem item)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Items.FirstOrDefault(i => i.Id == item.Id);
            if (existing != null)
            {
                existing.UpdateVisionMetadata(item.ExtractedText, item.ImageTags);
            }
        });
    }

    private bool MatchesFilter(ClipboardItem item)
    {
        if (SelectedFilter == "Textos" && item.Type == "Image") return false;
        if (SelectedFilter == "Imagens" && item.Type != "Image") return false;
        if (SelectedFilter == "Fixados" && !item.IsPinned) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchNorm = TextNormalizationHelper.RemoveDiacritics(SearchText.Trim());
            var itemNorm = item.NormalizedSearchContent ?? TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
            if (!itemNorm.Contains(searchNorm, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await LoadItemsAsync();
        UpdateStatusSubtitle();
        _clipboardService.Start();
    }

    [RelayCommand]
    public async Task LoadItemsAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _databaseService.GetItemsAsync(SelectedFilter, SearchText);
            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(new ClipboardItemViewModel(item));
            }
            UpdateStatusSubtitle();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao carregar itens: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SetFilterAsync(string filter)
    {
        if (SelectedFilter == filter) return;
        SelectedFilter = filter;
        OnPropertyChanged(nameof(FilterTodosBg));
        OnPropertyChanged(nameof(FilterTodosFg));
        OnPropertyChanged(nameof(FilterTextosBg));
        OnPropertyChanged(nameof(FilterTextosFg));
        OnPropertyChanged(nameof(FilterImagensBg));
        OnPropertyChanged(nameof(FilterImagensFg));
        OnPropertyChanged(nameof(FilterFixadosBg));
        OnPropertyChanged(nameof(FilterFixadosFg));
        await LoadItemsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadItemsAsync();
    }

    [RelayCommand]
    public async Task CopyItemAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        bool success = await _clipboardService.CopyToClipboardAsync(item.Model);
        if (success)
        {
            await ShowToastAsync("Copiado para a área de transferência");
        }
    }

    [RelayCommand]
    public async Task DeleteItemAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        await _databaseService.DeleteItemAsync(item.Model);
        Items.Remove(item);
        UpdateStatusSubtitle();
        await ShowToastAsync("Item removido do histórico");
    }

    [RelayCommand]
    public async Task TogglePinAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        await _databaseService.TogglePinAsync(item.Id);
        item.UpdatePinnedState(!item.IsPinned);
        await LoadItemsAsync();
        await ShowToastAsync(item.IsPinned ? "Item fixado no topo" : "Item desafixado");
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        int deleted = await _databaseService.ClearHistoryAsync(keepPinned: true);
        await LoadItemsAsync();
        await ShowToastAsync($"Histórico limpo ({deleted} itens removidos)");
    }

    [RelayCommand]
    public void ToggleViewMode()
    {
        IsGridView = !IsGridView;
        IsListView = !IsGridView;
        OnPropertyChanged(nameof(ViewModeIcon));
        OnPropertyChanged(nameof(ViewModeTooltip));
    }

    [RelayCommand]
    public async Task OpenItemViewerAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        if (Shell.Current != null)
        {
            await Shell.Current.Navigation.PushAsync(new ItemViewerPage(item));
        }
    }

    [RelayCommand]
    public async Task ToggleMonitoringAsync()
    {
        IsMonitoringPaused = !IsMonitoringPaused;
        _clipboardService.IsMonitoringPaused = IsMonitoringPaused;
        _trayService.UpdateStatus(IsMonitoringPaused);

        var msg = IsMonitoringPaused ? "Monitoramento pausado" : "Monitoramento ativo";
        await ShowToastAsync(msg);
        UpdateStatusSubtitle();
    }

    public void UpdateStatusSubtitle()
    {
        long totalBytes = _storageService.GetTotalStorageSize();
        string sizeStr = totalBytes < 1024 * 1024
            ? $"{totalBytes / 1024.0:F1} KB"
            : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

        StatusSubtitle = $"{Items.Count} itens • {sizeStr}";
    }

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        await Task.Delay(2200);
        IsToastVisible = false;
    }
}
