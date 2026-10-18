using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;

#if WINDOWS
using Windows.ApplicationModel.DataTransfer;
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
    private long _lastCaptureTimestamp;
    private bool _isInternalCopy;
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
            System.Diagnostics.Debug.WriteLine($"Falha ao iniciar listener do Windows: {ex.Message}");
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

        if (_isInternalCopy)
        {
            _isInternalCopy = false;
            return;
        }

        // 1. Debounce rígido de 500ms (ignora disparos repetidos da Ferramenta de Captura do Windows)
        long now = Environment.TickCount64;
        if (now - _lastCaptureTimestamp < 500)
        {
            return;
        }

        // 2. Prevenção de execução concorrente: se já estiver processando um frame, descarta
        if (!_captureLock.Wait(0))
        {
            return;
        }

        try
        {
            _lastCaptureTimestamp = Environment.TickCount64;

            var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (dataPackageView == null) return;

            // Prioridade 1: Imagem (Print Screen / Snipping Tool / Bitmap copiado)
            if (dataPackageView.Contains(StandardDataFormats.Bitmap))
            {
                await ProcessBitmapAsync(dataPackageView);
                return;
            }

            // Prioridade 2: Texto / URL / Código
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                await ProcessTextAsync(dataPackageView);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao processar área de transferência: {ex.Message}");
        }
        finally
        {
            _captureLock.Release();
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

            // Deduplicação estrita: comparar com último hash em memória e no banco
            if (_lastHash == null)
            {
                _lastHash = await _databaseService.GetLastItemHashAsync();
            }

            if (string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _lastHash = hash;

            // Classificar tipo
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
            System.Diagnostics.Debug.WriteLine($"Erro ao processar texto: {ex.Message}");
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
            using var memoryStream = new MemoryStream();
            using (var netStream = raStream.AsStreamForRead())
            {
                await netStream.CopyToAsync(memoryStream, _cts.Token);
            }

            var bytes = memoryStream.ToArray();
            if (bytes.Length == 0) return;

            // Verificação de Hash no buffer ANTES de salvar em disco
            var hash = _storageService.ComputeBytesHash(bytes);

            if (_lastHash == null)
            {
                _lastHash = await _databaseService.GetLastItemHashAsync();
            }

            // Descarta sumariamente se for idêntica ao último hash registrado
            if (string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _lastHash = hash;

            // Salvar no disco e registrar apenas se for único
            var filePath = await _storageService.SaveImageAsync(bytes, ".png");

            var item = new ClipboardItem
            {
                Type = "Image",
                FilePath = filePath,
                PreviewText = $"Imagem ({bytes.Length / 1024.0:F1} KB)",
                ContentHash = hash,
                ByteSize = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                IsPinned = false
            };

            await _databaseService.AddItemAsync(item);
            ItemCaptured?.Invoke(this, item);

            // Processamento em segundo plano de OCR (Windows) e Visão Computacional (ONNX)
            _ = Task.Run(async () =>
            {
                try
                {
                    bool hasUpdate = false;

                    // 1. OCR Nativo do Windows
                    var extractedText = await _ocrService.ExtractTextFromImageAsync(filePath, _cts.Token);
                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        item.ExtractedText = extractedText;
                        hasUpdate = true;
                    }

                    // 2. Classificação de Imagem ONNX MobileNetV2
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
                    System.Diagnostics.Debug.WriteLine($"Erro no processamento visual da imagem: {ex.Message}");
                }
            }, _cts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao processar imagem: {ex.Message}");
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

        _isInternalCopy = true;
        _lastHash = item.ContentHash;
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
            System.Diagnostics.Debug.WriteLine($"Erro ao copiar para Windows Clipboard: {ex.Message}");
            _isInternalCopy = false;
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
