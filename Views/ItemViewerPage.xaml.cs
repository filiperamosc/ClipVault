using System;
using System.Diagnostics;
using System.IO;
using ClipVault.Services;
using ClipVault.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;

namespace ClipVault.Views;

public partial class ItemViewerPage : ContentPage
{
    private readonly ClipboardItemViewModel _itemViewModel;
    private double _currentScale = 1;
    private double _startScale = 1;
    private double _xOffset = 0;
    private double _yOffset = 0;

    public ItemViewerPage(ClipboardItemViewModel itemViewModel)
    {
        InitializeComponent();
        _itemViewModel = itemViewModel;
        BindingContext = _itemViewModel;

        UpdateTextStats();
    }

    private void UpdateTextStats()
    {
        if (_itemViewModel.IsText && !string.IsNullOrEmpty(_itemViewModel.Content))
        {
            var text = _itemViewModel.Content;
            int chars = text.Length;
            int words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            TextStatsLabel.Text = $"{chars:N0} caracteres • {words:N0} palavras";

            if (_itemViewModel.Type == "Url" && Uri.TryCreate(text.Trim(), UriKind.Absolute, out _))
            {
                OpenBrowserButton.IsVisible = true;
            }
        }
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

    private async void OnCopyContentClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_itemViewModel.IsImage && !string.IsNullOrEmpty(_itemViewModel.FilePath) && File.Exists(_itemViewModel.FilePath))
            {
#if WINDOWS
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_itemViewModel.FilePath);
                dataPackage.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
#endif
            }
            else
            {
                await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(_itemViewModel.Content ?? string.Empty);
            }

            await DisplayAlert("Copiado!", "Conteúdo copiado para a área de transferência com sucesso.", "OK");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao copiar conteúdo: {ex.Message}");
        }
    }

    private async void OnCopyOcrClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_itemViewModel.ExtractedText))
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(_itemViewModel.ExtractedText);
            await DisplayAlert("Texto OCR Copiado!", "O texto extraído da imagem foi copiado com sucesso.", "OK");
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = TargetImage.Scale;
            TargetImage.AnchorX = 0.5;
            TargetImage.AnchorY = 0.5;
        }
        else if (e.Status == GestureStatus.Running)
        {
            _currentScale += (e.Scale - 1) * _startScale;
            _currentScale = Math.Clamp(_currentScale, 0.5, 6.0);
            TargetImage.Scale = _currentScale;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (TargetImage.Scale <= 1.0)
        {
            TargetImage.TranslationX = 0;
            TargetImage.TranslationY = 0;
            return;
        }

        if (e.StatusType == GestureStatus.Running)
        {
            TargetImage.TranslationX = _xOffset + e.TotalX;
            TargetImage.TranslationY = _yOffset + e.TotalY;
        }
        else if (e.StatusType == GestureStatus.Completed)
        {
            _xOffset = TargetImage.TranslationX;
            _yOffset = TargetImage.TranslationY;
        }
    }

    private void OnZoomInClicked(object? sender, EventArgs e)
    {
        _currentScale = Math.Clamp(_currentScale + 0.35, 0.5, 6.0);
        TargetImage.Scale = _currentScale;
    }

    private void OnZoomOutClicked(object? sender, EventArgs e)
    {
        _currentScale = Math.Clamp(_currentScale - 0.35, 0.5, 6.0);
        TargetImage.Scale = _currentScale;
        if (_currentScale <= 1.0)
        {
            TargetImage.TranslationX = 0;
            TargetImage.TranslationY = 0;
            _xOffset = 0;
            _yOffset = 0;
        }
    }

    private void OnResetZoomClicked(object? sender, EventArgs e)
    {
        _currentScale = 1.0;
        _xOffset = 0;
        _yOffset = 0;
        TargetImage.Scale = 1.0;
        TargetImage.TranslationX = 0;
        TargetImage.TranslationY = 0;
    }

    private void OnOpenExplorerClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_itemViewModel.FilePath) && File.Exists(_itemViewModel.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_itemViewModel.FilePath}\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao abrir explorer: {ex.Message}");
            }
        }
    }

    private async void OnOpenBrowserClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_itemViewModel.Content) &&
            Uri.TryCreate(_itemViewModel.Content.Trim(), UriKind.Absolute, out var uri))
        {
            try
            {
                await Launcher.Default.OpenAsync(uri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao abrir navegador: {ex.Message}");
            }
        }
    }
}
