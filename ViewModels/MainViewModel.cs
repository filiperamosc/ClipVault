using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;
using ClipVault.Services;
using ClipVault.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace ClipVault.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly IClipboardService _clipboardService;
    private readonly ITrayService _trayService;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _toastCts;

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

    // Garantir que apenas o CollectionView visível esteja conectado à ObservableCollection no WinUI 3
    public ObservableCollection<ClipboardItemViewModel>? ListItemsSource => IsListView ? Items : null;
    public ObservableCollection<ClipboardItemViewModel>? GridItemsSource => IsGridView ? Items : null;

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
        if (item == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Items.Any(i => i.Id == item.Id && item.Id != 0))
                {
                    return;
                }

                if (MatchesFilter(item))
                {
                    var vm = new ClipboardItemViewModel(item);
                    int insertIndex = 0;
                    while (insertIndex < Items.Count && Items[insertIndex].IsPinned)
                    {
                        insertIndex++;
                    }

                    if (insertIndex <= Items.Count)
                    {
                        Items.Insert(insertIndex, vm);
                    }
                    else
                    {
                        Items.Add(vm);
                    }
                }
                UpdateStatusSubtitle();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao inserir item na UI Thread: {ex.Message}");
            }
        });
    }

    private void OnItemUpdated(object? sender, ClipboardItem item)
    {
        if (item == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var existing = Items.FirstOrDefault(i => i.Id == item.Id);
                if (existing != null)
                {
                    existing.UpdateVisionMetadata(item.ExtractedText, item.ImageTags);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar metadados na UI Thread: {ex.Message}");
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
        _clipboardService.Start();
    }

    [RelayCommand]
    public async Task LoadItemsAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            string filterSnapshot = SelectedFilter;
            string searchSnapshot = SearchText;

            var list = await _databaseService.GetItemsAsync(filterSnapshot, searchSnapshot);
            var viewModels = list.Select(item => new ClipboardItemViewModel(item)).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    IsBusy = true;
                    Items.Clear();
                    foreach (var vm in viewModels)
                    {
                        Items.Add(vm);
                    }
                    UpdateStatusSubtitle();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao sincronizar coleção na UI Thread: {ex.Message}");
                }
                finally
                {
                    IsBusy = false;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao carregar itens do banco: {ex.Message}");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    [RelayCommand]
    public async Task SetFilterAsync(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || SelectedFilter == filter) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectedFilter = filter;
            OnPropertyChanged(nameof(FilterTodosBg));
            OnPropertyChanged(nameof(FilterTodosFg));
            OnPropertyChanged(nameof(FilterTextosBg));
            OnPropertyChanged(nameof(FilterTextosFg));
            OnPropertyChanged(nameof(FilterImagensBg));
            OnPropertyChanged(nameof(FilterImagensFg));
            OnPropertyChanged(nameof(FilterFixadosBg));
            OnPropertyChanged(nameof(FilterFixadosFg));
        });

        await LoadItemsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        try
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            var cts = new CancellationTokenSource();
            _searchDebounceCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        await LoadItemsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
        catch { }
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Items.Contains(item))
                {
                    Items.Remove(item);
                }
                UpdateStatusSubtitle();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao remover item na UI Thread: {ex.Message}");
            }
        });

        await ShowToastAsync("Item removido do histórico");
    }

    [RelayCommand]
    public async Task TogglePinAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;

        await _databaseService.TogglePinAsync(item.Id);
        bool newPinnedState = !item.IsPinned;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                item.UpdatePinnedState(newPinnedState);

                int currentIndex = Items.IndexOf(item);
                if (currentIndex < 0) return;

                if (SelectedFilter == "Fixados" && !newPinnedState)
                {
                    Items.RemoveAt(currentIndex);
                    UpdateStatusSubtitle();
                    return;
                }

                // Calcular nova posição ordenada sem destruir a árvore visual (sem Items.Clear)
                int targetIndex = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (ReferenceEquals(Items[i], item)) continue;

                    var other = Items[i];
                    bool otherComesBefore = false;

                    if (other.IsPinned && !newPinnedState)
                    {
                        otherComesBefore = true;
                    }
                    else if (other.IsPinned == newPinnedState && other.Model.CreatedAt > item.Model.CreatedAt)
                    {
                        otherComesBefore = true;
                    }

                    if (otherComesBefore)
                    {
                        targetIndex++;
                    }
                }

                targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, Items.Count - 1));
                if (currentIndex != targetIndex)
                {
                    Items.Move(currentIndex, targetIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao reordenar item fixado na UI Thread: {ex.Message}");
            }
        });

        await ShowToastAsync(newPinnedState ? "Item fixado no topo" : "Item desafixado");
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                bool targetGrid = !IsGridView;

                // Desconectar temporariamente o CollectionView anterior antes de exibir o novo
                IsGridView = targetGrid;
                IsListView = !targetGrid;

                OnPropertyChanged(nameof(ListItemsSource));
                OnPropertyChanged(nameof(GridItemsSource));
                OnPropertyChanged(nameof(ViewModeIcon));
                OnPropertyChanged(nameof(ViewModeTooltip));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao alternar modo de exibição na UI Thread: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    public async Task OpenItemViewerAsync(ClipboardItemViewModel item)
    {
        if (item == null) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.Navigation.PushAsync(new ItemViewerPage(item));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao abrir visualizador: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    public async Task ToggleMonitoringAsync()
    {
        bool paused = !IsMonitoringPaused;
        _clipboardService.IsMonitoringPaused = paused;
        _trayService.UpdateStatus(paused);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsMonitoringPaused = paused;
            UpdateStatusSubtitle();
        });

        var msg = paused ? "Monitoramento pausado" : "Monitoramento ativo";
        await ShowToastAsync(msg);
    }

    public void UpdateStatusSubtitle()
    {
        long totalBytes = _storageService.GetTotalStorageSize();
        string sizeStr = totalBytes < 1024 * 1024
            ? $"{totalBytes / 1024.0:F1} KB"
            : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

        string subtitle = $"{Items.Count} itens • {sizeStr}";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusSubtitle = subtitle;
        });
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            _toastCts?.Cancel();
            _toastCts?.Dispose();
            var cts = new CancellationTokenSource();
            _toastCts = cts;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ToastMessage = message;
                IsToastVisible = true;
            });

            await Task.Delay(2200, cts.Token);

            if (!cts.Token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsToastVisible = false;
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
