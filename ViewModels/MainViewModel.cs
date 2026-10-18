using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipVault.Models;
using ClipVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;

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

        _trayService.RequestClearHistory += () => MainThread.BeginInvokeOnMainThread(async () => await ClearHistoryAsync());
        _trayService.RequestToggleMonitoring += () => MainThread.BeginInvokeOnMainThread(async () => await ToggleMonitoringAsync());
    }

    private void OnItemCaptured(object? sender, ClipboardItem item)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Se passar no filtro atual, insere no topo da lista
            if (MatchesFilter(item))
            {
                var vm = new ClipboardItemViewModel(item);
                // Inserir após os fixados, ou no índice 0 se nenhum fixado
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

    private bool MatchesFilter(ClipboardItem item)
    {
        if (SelectedFilter == "Textos" && item.Type == "Image") return false;
        if (SelectedFilter == "Imagens" && item.Type != "Image") return false;
        if (SelectedFilter == "Fixados" && !item.IsPinned) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            bool match = (item.Content?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                         (item.PreviewText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!match) return false;
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
            await ShowToastAsync("Copiado para a área de transferência com sucesso! 📋");
        }
    }

    [RelayCommand]
    public async Task DeleteItemAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        await _databaseService.DeleteItemAsync(item.Model);
        Items.Remove(item);
        UpdateStatusSubtitle();
        await ShowToastAsync("Item excluído do histórico.");
    }

    [RelayCommand]
    public async Task TogglePinAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        await _databaseService.TogglePinAsync(item.Id);
        item.UpdatePinnedState(!item.IsPinned);
        await LoadItemsAsync(); // Recarrega para manter ordenação com fixados no topo
        await ShowToastAsync(item.IsPinned ? "Item fixado no topo 📌" : "Item desafixado 📍");
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        int deleted = await _databaseService.ClearHistoryAsync(keepPinned: true);
        await LoadItemsAsync();
        await ShowToastAsync($"Histórico limpo ({deleted} itens não fixados removidos).");
    }

    [RelayCommand]
    public async Task ToggleMonitoringAsync()
    {
        IsMonitoringPaused = !IsMonitoringPaused;
        _clipboardService.IsMonitoringPaused = IsMonitoringPaused;
        _trayService.UpdateStatus(IsMonitoringPaused);

        var msg = IsMonitoringPaused ? "Monitoramento pausado ⏸️" : "Monitoramento ativado ▶️";
        await ShowToastAsync(msg);
        UpdateStatusSubtitle();
    }

    public void UpdateStatusSubtitle()
    {
        long totalBytes = _storageService.GetTotalStorageSize();
        string sizeStr = totalBytes < 1024 * 1024
            ? $"{totalBytes / 1024.0:F1} KB"
            : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

        StatusSubtitle = $"{Items.Count} itens exibidos | Armazenamento: {sizeStr} {(IsMonitoringPaused ? "• PAUSADO" : "• ATIVO")}";
    }

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        await Task.Delay(2500);
        IsToastVisible = false;
    }
}
