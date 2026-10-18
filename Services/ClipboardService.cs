using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;

#if WINDOWS
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace ClipVault.Services;

public class ClipboardService : IClipboardService
{
    private readonly IDatabaseService _databaseService;
    private readonly IStorageService _storageService;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private string? _lastHash;
    private string? _lastImageHash;
    private long _lastCaptureTimestamp;
    private long _ignoreInternalUntilTimestamp;
    private bool _isStarted;
    private bool _isDisposed;

    public event EventHandler<ClipboardItem>? ItemCaptured;
    public event EventHandler<ClipboardItem>? ItemUpdated;
    public bool IsMonitoringPaused { get; set; }

    private readonly IOcrService _ocrService;
    private readonly IVisionClassifierService _visionClassifierService;

    public ClipboardService(
        IDatabaseService databaseService,
        IStorageService storageService,
        IOcrService ocrService,
        IVisionClassifierService visionClassifierService)
    {
        _databaseService = databaseService;
        _storageService = storageService;
        _ocrService = ocrService;
        _visionClassifierService = visionClassifierService;
    }

    public void Start()
    {
        if (_isStarted || _isDisposed) return;
        _isStarted = true;

#if WINDOWS
        try
        {
            Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged += OnWindowsClipboardContentChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao iniciar listener do Windows: {ex.Message}");
        }
#endif
    }

    public void Stop()
    {
        if (!_isStarted) return;
        _isStarted = false;

#if WINDOWS
        try
        {
            Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged -= OnWindowsClipboardContentChanged;
        }
        catch { }
#endif
    }

#if WINDOWS
    private async void OnWindowsClipboardContentChanged(object? sender, object e)
    {
        if (_isDisposed || _cts.IsCancellationRequested || IsMonitoringPaused) return;

        long now = Environment.TickCount64;
        if (now < _ignoreInternalUntilTimestamp)
        {
            return;
        }

        // Debounce temporal inicial para rajadas simultâneas (< 350ms)
        if (now - _lastCaptureTimestamp < 350)
        {
            return;
        }

        // Garantir execução estritamente sequencial para que a verificação de Hash nunca sofra condição de corrida
        if (!_captureLock.Wait(0))
        {
            return;
        }

        try
        {
            _lastCaptureTimestamp = Environment.TickCount64;

            // Pequeno atraso de estabilização (80ms) para que a Ferramenta de Captura do Windows conclua a escrita do buffer
            await Task.Delay(80, _cts.Token);

            var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (dataPackageView == null) return;

            if (dataPackageView.Contains(StandardDataFormats.Bitmap))
            {
                await ProcessBitmapAsync(dataPackageView);
                return;
            }

            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                await ProcessTextAsync(dataPackageView);
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao processar área de transferência: {ex.Message}");
        }
        finally
        {
            try
            {
                _captureLock.Release();
            }
            catch { }
        }
    }

    private async Task ProcessTextAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackageView)
    {
        try
        {
            if (_cts.IsCancellationRequested) return;

            var text = await dataPackageView.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            var hash = _storageService.ComputeTextHash(text.Trim());
            if (string.IsNullOrEmpty(hash)) return;

            if (_lastHash == null)
            {
                _lastHash = await _databaseService.GetLastItemHashAsync();
            }

            if (string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase) ||
                await _databaseService.IsDuplicateRecentHashAsync(hash, withinSeconds: 15))
            {
                Debug.WriteLine("Texto duplicado consecutivo descartado pelo filtro de Hash.");
                return;
            }

            _lastHash = hash;
            _lastCaptureTimestamp = Environment.TickCount64;

            var type = "Text";
            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                type = "Url";
            }
            else if (IsLikelyCode(text))
            {
                type = "Code";
            }

            var preview = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
            preview = preview.Replace("\r\n", " ").Replace("\n", " ");

            var item = new ClipboardItem
            {
                Type = type,
                Content = text,
                PreviewText = preview,
                ContentHash = hash,
                ByteSize = System.Text.Encoding.UTF8.GetByteCount(text),
                CreatedAt = DateTime.UtcNow,
                IsPinned = false
            };

            await _databaseService.AddItemAsync(item);
            ItemCaptured?.Invoke(this, item);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao processar texto: {ex.Message}");
        }
    }

    private async Task ProcessBitmapAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackageView)
    {
        try
        {
            if (_cts.IsCancellationRequested) return;

            var streamRef = await dataPackageView.GetBitmapAsync();
            if (streamRef == null) return;

            using var raStream = await streamRef.OpenReadAsync();
            if (raStream == null || raStream.Size == 0) return;

            byte[] bytesToSave;
            string hash;

            try
            {
                // Decodificar os pixels reais da imagem para calcular o Hash SHA-256 dos canais RGB puros.
                // Isso impede que a Ferramenta de Captura do Windows gere hashes diferentes ao regravar
                // cabeçalhos DIB/BMP vs PNG do mesmo print em eventos subsequentes.
                var decoder = await BitmapDecoder.CreateAsync(raStream);
                uint width = decoder.PixelWidth;
                uint height = decoder.PixelHeight;

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] rawPixels = pixelData.DetachPixelData();
                if (rawPixels == null || rawPixels.Length == 0) return;

                // Normalizar canal Alpha para 255 em todos os pixels antes do Hash e da codificação PNG
                for (int i = 3; i < rawPixels.Length; i += 4)
                {
                    rawPixels[i] = 255;
                }

                hash = _storageService.ComputeBytesHash(rawPixels);

                // Verificar duplicidade ANTES de codificar PNG ou gravar em disco
                if (_lastHash == null)
                {
                    _lastHash = await _databaseService.GetLastItemHashAsync();
                }

                if (string.Equals(hash, _lastImageHash, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase) ||
                    await _databaseService.IsDuplicateRecentHashAsync(hash, withinSeconds: 30))
                {
                    Debug.WriteLine($"Captura de imagem duplicada descartada (SHA-256 idêntico: {hash[..12]}...).");
                    return;
                }

                // Codificar para formato PNG real em memória
                using var pngRandomAccessStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngRandomAccessStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    width,
                    height,
                    96.0,
                    96.0,
                    rawPixels);
                await encoder.FlushAsync();

                pngRandomAccessStream.Seek(0);
                using var ms = new MemoryStream();
                await pngRandomAccessStream.AsStreamForRead().CopyToAsync(ms, _cts.Token);
                bytesToSave = ms.ToArray();
            }
            catch
            {
                raStream.Seek(0);
                using var fallbackMs = new MemoryStream();
                await raStream.AsStreamForRead().CopyToAsync(fallbackMs, _cts.Token);
                bytesToSave = fallbackMs.ToArray();
                if (bytesToSave.Length == 0) return;

                hash = _storageService.ComputeBytesHash(bytesToSave);

                if (_lastHash == null)
                {
                    _lastHash = await _databaseService.GetLastItemHashAsync();
                }

                if (string.Equals(hash, _lastImageHash, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase) ||
                    await _databaseService.IsDuplicateRecentHashAsync(hash, withinSeconds: 30))
                {
                    return;
                }
            }

            _lastImageHash = hash;
            _lastHash = hash;
            _lastCaptureTimestamp = Environment.TickCount64;

            var filePath = await _storageService.SaveImageAsync(bytesToSave, ".png");

            var item = new ClipboardItem
            {
                Type = "Image",
                FilePath = filePath,
                PreviewText = $"Imagem ({bytesToSave.Length / 1024.0:F1} KB)",
                ContentHash = hash,
                ByteSize = bytesToSave.Length,
                CreatedAt = DateTime.UtcNow,
                IsPinned = false
            };

            await _databaseService.AddItemAsync(item);
            ItemCaptured?.Invoke(this, item);

            // Processamento em segundo plano de OCR (Windows) e Classificação Visual (ONNX)
            _ = Task.Run(async () =>
            {
                try
                {
                    bool hasUpdate = false;

                    var extractedText = await _ocrService.ExtractTextFromImageAsync(filePath, _cts.Token);
                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        item.ExtractedText = extractedText;
                        hasUpdate = true;
                    }

                    var tags = await _visionClassifierService.ClassifyImageAsync(filePath, _cts.Token);
                    if (!string.IsNullOrWhiteSpace(tags))
                    {
                        item.ImageTags = tags;
                        hasUpdate = true;
                    }

                    if (hasUpdate && !_cts.IsCancellationRequested)
                    {
                        await _databaseService.UpdateItemAsync(item);
                        ItemUpdated?.Invoke(this, item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro no processamento visual da imagem: {ex.Message}");
                }
            }, _cts.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao processar imagem: {ex.Message}");
        }
    }
#endif

    private static bool IsLikelyCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("{") && text.Contains("}")) return true;
        if (text.Contains("class ") || text.Contains("function ") || text.Contains("def ") || text.Contains("const ")) return true;
        if (text.Contains("</") || text.Contains("/>")) return true;
        if (text.Contains("SELECT ") && text.Contains("FROM ")) return true;
        return false;
    }

    public async Task<bool> CopyToClipboardAsync(ClipboardItem item)
    {
        if (item == null) return false;

        _ignoreInternalUntilTimestamp = Environment.TickCount64 + 1200;
        if (!string.IsNullOrEmpty(item.ContentHash))
        {
            _lastHash = item.ContentHash;
            if (item.IsImage)
            {
                _lastImageHash = item.ContentHash;
            }
        }
        _lastCaptureTimestamp = Environment.TickCount64;

#if WINDOWS
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

            if (item.IsImage && !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            }
            else
            {
                dataPackage.SetText(item.Content ?? string.Empty);
            }

            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao copiar para Windows Clipboard: {ex.Message}");
            _ignoreInternalUntilTimestamp = 0;
            return false;
        }
#else
        try
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(item.Content ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
#endif
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            Stop();
            _captureLock.Dispose();
            _cts.Dispose();
        }
        catch { }
    }
}
