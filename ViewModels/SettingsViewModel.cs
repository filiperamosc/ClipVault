using System;
using System.IO;
using System.Threading.Tasks;
using ClipVault.Models;
using ClipVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

#if WINDOWS
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace ClipVault.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoCleanService _autoCleanService;
    private readonly IStorageService _storageService;

    [ObservableProperty]
    private string _storageDirectory = string.Empty;

    [ObservableProperty]
    private bool _enableAutoCleanByDays;

    [ObservableProperty]
    private int _autoCleanDays;

    [ObservableProperty]
    private bool _enableAutoCleanBySize;

    [ObservableProperty]
    private int _maxStorageMb;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _currentStorageSizeFormatted = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusBackgroundColor = "#22C55E";

    public SettingsViewModel(
        ISettingsService settingsService,
        IAutoCleanService autoCleanService,
        IStorageService storageService)
    {
        _settingsService = settingsService;
        _autoCleanService = autoCleanService;
        _storageService = storageService;

        LoadFromConfig();
    }

    public void LoadFromConfig()
    {
        var cfg = _settingsService.CurrentConfig;
        StorageDirectory = cfg.StorageDirectory;
        EnableAutoCleanByDays = cfg.EnableAutoCleanByDays;
        AutoCleanDays = cfg.AutoCleanDays;
        EnableAutoCleanBySize = cfg.EnableAutoCleanBySize;
        MaxStorageMb = cfg.MaxStorageMb;
        StartWithWindows = _settingsService.CheckStartWithWindows();

        UpdateStorageSize();
    }

    public void UpdateStorageSize()
    {
        _ = Task.Run(() =>
        {
            var totalBytes = _storageService.GetTotalStorageSize();
            var formatted = totalBytes < 1024 * 1024
                ? $"{totalBytes / 1024.0:F1} KB"
                : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentStorageSizeFormatted = formatted;
            });
        });
    }

    [RelayCommand]
    public async Task PickFolderAsync()
    {
#if WINDOWS
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            // Obter HWND da janela ativa
            if (Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0)
            {
                var mauiWindow = Microsoft.Maui.Controls.Application.Current.Windows[0];
                if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
                {
                    var hwnd = WindowNative.GetWindowHandle(winuiWindow);
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }
            }

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null && !string.IsNullOrWhiteSpace(folder.Path))
            {
                StorageDirectory = folder.Path;
                await ShowStatusAsync("Pasta selecionada no campo. Clique em 'Salvar Alterações' para aplicar. 💡", isError: false);
            }
        }
        catch (Exception ex)
        {
            await ShowStatusAsync($"Não foi possível selecionar a pasta: {ex.Message}", isError: true);
        }
#else
        await ShowStatusAsync("Seleção de pasta disponível no Windows.", isError: false);
#endif
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var cfg = _settingsService.CurrentConfig;
            cfg.StorageDirectory = StorageDirectory;
            cfg.EnableAutoCleanByDays = EnableAutoCleanByDays;
            cfg.AutoCleanDays = AutoCleanDays;
            cfg.EnableAutoCleanBySize = EnableAutoCleanBySize;
            cfg.MaxStorageMb = MaxStorageMb;

            _settingsService.SaveConfig(cfg);
            _settingsService.SetStartWithWindows(StartWithWindows);

            UpdateStorageSize();
            await ShowStatusAsync("Configurações salvas com sucesso! ✔️", isError: false);
        }
        catch (Exception ex)
        {
            await ShowStatusAsync($"Erro ao salvar: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task ExecuteCleanNowAsync()
    {
        try
        {
            int removed = await _autoCleanService.ExecuteCleanNowAsync();
            UpdateStorageSize();
            await ShowStatusAsync($"Limpeza concluída! {removed} itens antigos/excedentes removidos.", isError: false);
        }
        catch (Exception ex)
        {
            await ShowStatusAsync($"Erro na limpeza: {ex.Message}", isError: true);
        }
    }

    private async Task ShowStatusAsync(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusBackgroundColor = isError ? "#EF4444" : "#22C55E";
        IsStatusVisible = true;
        await Task.Delay(3000);
        IsStatusVisible = false;
    }
}
