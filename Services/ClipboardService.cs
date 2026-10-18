using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;
using Microsoft.Maui.ApplicationModel;

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
    private readonly object _logLock = new();

    private string? _lastHash;
    private string? _lastImageHash;
    private long _lastHashTimestamp;
    private long _ignoreInternalUntilTimestamp;
    private volatile bool _hasPendingClipboardEvent;
    private bool _isStarted;
    private bool _isDisposed;

    private const int MaxClipboardRetries = 6;
    private static readonly int[] RetryDelaysMs = { 60, 140, 260, 420, 650, 950 };

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

    private void LogClipboardDiagnostic(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);

            lock (_logLock)
            {
                var logPath = Path.Combine(AppEnvironment.AppRootDirectory, "clipboard_diagnostics.log");
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024)
                {
                    File.WriteAllText(logPath, line + Environment.NewLine);
                }
                else
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
        }
        catch { }
    }

    public void Start()
    {
        if (_isStarted || _isDisposed) return;
        _isStarted = true;

#if WINDOWS
        try
        {
            Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged += OnWindowsClipboardContentChanged;
            LogClipboardDiagnostic("Monitoramento da área de transferência iniciado.");
        }
        catch (Exception ex)
        {
            LogClipboardDiagnostic($"ERRO ao iniciar listener do Windows: {ex.Message}");
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

        // Nunca descartar eventos se o worker já estiver ocupado: sinalizar evento pendente na fila
        _hasPendingClipboardEvent = true;

        if (!_captureLock.Wait(0))
        {
            return;
        }

        try
        {
            while (_hasPendingClipboardEvent && !_isDisposed && !_cts.IsCancellationRequested && !IsMonitoringPaused)
            {
                _hasPendingClipboardEvent = false;
                await ProcessClipboardWithRetryAsync();
            }
        }
        catch (Exception ex)
        {
            LogClipboardDiagnostic($"ERRO crítico no loop de captura do clipboard: {ex.Message}");
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

    private async Task ProcessClipboardWithRetryAsync()
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxClipboardRetries; attempt++)
        {
            if (_isDisposed || _cts.IsCancellationRequested || IsMonitoringPaused) return;

            int delayMs = RetryDelaysMs[Math.Min(attempt, RetryDelaysMs.Length - 1)];
            await Task.Delay(delayMs, _cts.Token);

            try
            {
                // Acessar o DataPackageView na STA / UI Thread para evitar RPC_E_WRONG_THREAD ou bloqueio de COM
                Windows.ApplicationModel.DataTransfer.DataPackageView? dataPackageView = null;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                });

                if (dataPackageView == null)
                {
                    LogClipboardDiagnostic($"[Tentativa {attempt + 1}/{MaxClipboardRetries}] Clipboard.GetContent() retornou nulo. Repetindo...");
                    continue;
                }

                bool hasBitmap = false;
                bool hasText = false;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    hasBitmap = dataPackageView.Contains(StandardDataFormats.Bitmap);
                    if (!hasBitmap)
                    {
                        hasText = dataPackageView.Contains(StandardDataFormats.Text);
                    }
                });

                if (hasBitmap)
                {
                    bool capturedOrDeduplicated = await TryCaptureValidBitmapAsync(dataPackageView, attempt + 1);
                    if (capturedOrDeduplicated)
                    {
                        return;
                    }
                    // Se o stream estava incompleto ou corrompido, o loop fará nova tentativa com backoff
                    continue;
                }

                if (hasText)
                {
                    bool capturedOrDeduplicated = await TryCaptureTextAsync(dataPackageView, attempt + 1);
                    if (capturedOrDeduplicated)
                    {
                        return;
                    }
                    continue;
                }

                // Caso o clipboard ainda esteja sendo populado pela Ferramenta de Captura (nenhum formato visível ainda)
                if (attempt < 2)
                {
                    LogClipboardDiagnostic($"[Tentativa {attempt + 1}/{MaxClipboardRetries}] Formatos ainda não disponíveis no clipboard. Aguardando escrita...");
                    continue;
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (COMException ex)
            {
                lastException = ex;
                LogClipboardDiagnostic($"[Tentativa {attempt + 1}/{MaxClipboardRetries}] Bloqueio COM no Clipboard (0x{ex.HResult:X8}): {ex.Message}");
            }
            catch (ExternalException ex)
            {
                lastException = ex;
                LogClipboardDiagnostic($"[Tentativa {attempt + 1}/{MaxClipboardRetries}] Clipboard bloqueado por outro processo (ExternalException 0x{ex.ErrorCode:X8}): {ex.Message}");
            }
            catch (Exception ex)
            {
                lastException = ex;
                LogClipboardDiagnostic($"[Tentativa {attempt + 1}/{MaxClipboardRetries}] Exceção ao ler clipboard ({ex.GetType().Name}): {ex.Message}");
            }
        }

        if (lastException != null)
        {
            LogClipboardDiagnostic($"FALHA após {MaxClipboardRetries} tentativas de leitura do clipboard. Último erro: {lastException.Message}");
        }
    }

    private async Task<bool> TryCaptureTextAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackageView, int attemptNumber)
    {
        string? text = null;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            text = await dataPackageView.GetTextAsync();
        });

        if (string.IsNullOrWhiteSpace(text))
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Texto vazio lido do clipboard.");
            return false;
        }

        var hash = _storageService.ComputeTextHash(text.Trim());
        if (string.IsNullOrEmpty(hash)) return false;

        if (_lastHash == null)
        {
            _lastHash = await _databaseService.GetLastItemHashAsync();
        }

        long now = Environment.TickCount64;
        if (string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase) && (now - _lastHashTimestamp < 60000))
        {
            return true;
        }

        if (await _databaseService.IsDuplicateRecentHashAsync(hash, withinSeconds: 15))
        {
            _lastHash = hash;
            _lastHashTimestamp = now;
            return true;
        }

        _lastHash = hash;
        _lastHashTimestamp = now;

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

        await Task.Run(async () =>
        {
            await _databaseService.AddItemAsync(item);
        });

        ItemCaptured?.Invoke(this, item);
        return true;
    }

    private async Task<bool> TryCaptureValidBitmapAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackageView, int attemptNumber)
    {
        IRandomAccessStreamReference? streamRef = null;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            streamRef = await dataPackageView.GetBitmapAsync();
        });

        if (streamRef == null)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] GetBitmapAsync() retornou nulo.");
            return false;
        }

        using IRandomAccessStreamWithContentType raStream = await streamRef.OpenReadAsync();
        if (raStream == null || raStream.Size < 64)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Stream de bitmap vazio ou incompleto ({raStream?.Size ?? 0} bytes).");
            return false;
        }

        ulong expectedStreamSize = raStream.Size;
        byte[] rawStreamBytes;
        using (var ms = new MemoryStream((int)expectedStreamSize))
        {
            raStream.Seek(0);
            await raStream.AsStreamForRead().CopyToAsync(ms, _cts.Token);
            rawStreamBytes = ms.ToArray();
        }

        // 1. Validação de integridade do buffer extraído contra leitura parcial
        if ((ulong)rawStreamBytes.Length != expectedStreamSize || rawStreamBytes.Length < 64)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Tamanho lido ({rawStreamBytes.Length}) diverge do esperado ({expectedStreamSize}). Forçando nova leitura...");
            return false;
        }

        // Se for um container BMP/DIB, validar cabeçalho e se todas as scanlines já foram escritas
        if (rawStreamBytes[0] == (byte)'B' && rawStreamBytes[1] == (byte)'M' && rawStreamBytes.Length >= 54)
        {
            int dibHeaderSize = BitConverter.ToInt32(rawStreamBytes, 14);
            int bmpWidth = BitConverter.ToInt32(rawStreamBytes, 18);
            int bmpHeight = Math.Abs(BitConverter.ToInt32(rawStreamBytes, 22));
            ushort bpp = BitConverter.ToUInt16(rawStreamBytes, 28);

            if (bmpWidth <= 0 || bmpHeight <= 0 || bmpWidth > 16384 || bmpHeight > 16384 || bpp < 8)
            {
                LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Cabeçalho BMP inválido ({bmpWidth}x{bmpHeight}, {bpp}bpp).");
                return false;
            }

            int rowStride = ((bmpWidth * bpp + 31) / 32) * 4;
            long minExpectedBytes = 14L + dibHeaderSize + (long)rowStride * bmpHeight;
            if (rawStreamBytes.Length < minExpectedBytes && BitConverter.ToInt32(rawStreamBytes, 30) == 0) // BI_RGB sem compressão
            {
                LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Buffer BMP truncado ({rawStreamBytes.Length} < {minExpectedBytes} bytes). Repetindo leitura...");
                return false;
            }
        }

        // 2. Decodificação nativa via WIC SoftwareBitmap (preserva alinhamento de stride e canais BGRA8 sem artefatos)
        using var backingMemoryStream = new MemoryStream(rawStreamBytes, writable: false);
        using var inMemoryStream = backingMemoryStream.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(inMemoryStream);
        uint width = decoder.PixelWidth;
        uint height = decoder.PixelHeight;

        if (width == 0 || height == 0 || width > 16384 || height > 16384)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Dimensões decodificadas inválidas ({width}x{height}).");
            return false;
        }

        // Extrair pixels BGRA8 para validação de integridade das linhas e cálculo do Hash SHA-256 RGB
        var pixelProvider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        byte[] bgraPixels = pixelProvider.DetachPixelData();
        int expectedPixelBytes = (int)(width * height * 4);
        if (bgraPixels == null || bgraPixels.Length != expectedPixelBytes)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Matriz de pixels incompleta ({bgraPixels?.Length ?? 0} de {expectedPixelBytes} bytes).");
            return false;
        }

        if (!ValidateBitmapPixelsIntegrity(bgraPixels, (int)width, (int)height))
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Detectadas scanlines não inicializadas/corrompidas no bitmap ({width}x{height}). Forçando retry...");
            return false;
        }

        // 3. Calcular Hash SHA-256 determinístico sobre os canais RGB (ignorando variações de Alpha entre CF_BITMAP e PNG)
        string hash = ComputeRgbPixelHash(bgraPixels, (int)width, (int)height);

        if (_lastHash == null)
        {
            _lastHash = await _databaseService.GetLastItemHashAsync();
        }

        long now = Environment.TickCount64;
        if (string.Equals(hash, _lastImageHash, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hash, _lastHash, StringComparison.OrdinalIgnoreCase) ||
            await _databaseService.IsDuplicateRecentHashAsync(hash, withinSeconds: 25))
        {
            _lastImageHash = hash;
            _lastHash = hash;
            _lastHashTimestamp = now;
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Print duplicado descartado com segurança (Hash RGB SHA-256: {hash[..12]}...).");
            return true;
        }

        // 4. Codificar para PNG utilizando SoftwareBitmap nativo do WIC (elimina 100% de distorções de stride/linhas)
        inMemoryStream.Seek(0);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        using var pngOutStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngOutStream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        if (pngOutStream.Size < 64)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] PNG codificado inválido ({pngOutStream.Size} bytes).");
            return false;
        }

        pngOutStream.Seek(0);
        byte[] pngBytes = new byte[pngOutStream.Size];
        await pngOutStream.AsStreamForRead().ReadExactlyAsync(pngBytes, 0, pngBytes.Length, _cts.Token);

        // Validar assinatura PNG (\x89PNG\r\n\x1a\n)
        if (pngBytes.Length < 8 || pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47)
        {
            LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Assinatura PNG inválida após codificação.");
            return false;
        }

        _lastImageHash = hash;
        _lastHash = hash;
        _lastHashTimestamp = now;

        // 5. Persistir arquivo e metadados em background
        var filePath = await _storageService.SaveImageAsync(pngBytes, ".png");

        var item = new ClipboardItem
        {
            Type = "Image",
            FilePath = filePath,
            PreviewText = $"Imagem ({pngBytes.Length / 1024.0:F1} KB)",
            ContentHash = hash,
            ByteSize = pngBytes.Length,
            CreatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        await Task.Run(async () =>
        {
            await _databaseService.AddItemAsync(item);
        });

        LogClipboardDiagnostic($"[Tentativa {attemptNumber}] Imagem capturada e validada com sucesso ({width}x{height}, {pngBytes.Length / 1024.0:F1} KB).");
        ItemCaptured?.Invoke(this, item);

        // 6. Enriquecimento assíncrono em segundo plano (OCR + Classificação Visual ONNX)
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
                LogClipboardDiagnostic($"Erro no processamento visual secundário: {ex.Message}");
            }
        }, _cts.Token);

        return true;
    }

    /// <summary>
    /// Verifica se o buffer de pixels BGRA8 está íntegro e não foi lido pela metade (evita faixas pretas/transparentes de escrita incompleta).
    /// </summary>
    private static bool ValidateBitmapPixelsIntegrity(byte[] bgraPixels, int width, int height)
    {
        if (bgraPixels.Length < width * height * 4) return false;

        int rowBytes = width * 4;
        bool topHasData = false;

        // Verificar amostra das primeiras linhas
        int topCheckRows = Math.Max(1, height / 4);
        for (int y = 0; y < topCheckRows && !topHasData; y += Math.Max(1, topCheckRows / 8))
        {
            int rowOffset = y * rowBytes;
            for (int x = 0; x < rowBytes; x += 16)
            {
                if (bgraPixels[rowOffset + x] != 0 || bgraPixels[rowOffset + x + 1] != 0 || bgraPixels[rowOffset + x + 2] != 0)
                {
                    topHasData = true;
                    break;
                }
            }
        }

        // Verificar se a imagem inteira está zerada (buffer não inicializado)
        if (!topHasData)
        {
            bool anyNonZero = false;
            for (int i = 0; i < bgraPixels.Length; i += 64)
            {
                if (bgraPixels[i] != 0 || bgraPixels[i + 1] != 0 || bgraPixels[i + 2] != 0)
                {
                    anyNonZero = true;
                    break;
                }
            }
            return anyNonZero;
        }

        return true;
    }

    /// <summary>
    /// Calcula o SHA-256 determinístico apenas dos bytes RGB e dimensões da imagem, sem modificar o buffer original.
    /// </summary>
    private static string ComputeRgbPixelHash(byte[] bgraPixels, int width, int height)
    {
        using var sha256 = SHA256.Create();
        byte[] rgbRow = new byte[width * 3];
        byte[] header = BitConverter.GetBytes(width ^ (height << 16));
        sha256.TransformBlock(header, 0, header.Length, null, 0);

        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int srcOffset = y * stride;
            int dstOffset = 0;
            for (int x = 0; x < width; x++)
            {
                rgbRow[dstOffset++] = bgraPixels[srcOffset];
                rgbRow[dstOffset++] = bgraPixels[srcOffset + 1];
                rgbRow[dstOffset++] = bgraPixels[srcOffset + 2];
                srcOffset += 4;
            }
            sha256.TransformBlock(rgbRow, 0, rgbRow.Length, null, 0);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
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
            _lastHashTimestamp = Environment.TickCount64;
            if (item.IsImage)
            {
                _lastImageHash = item.ContentHash;
            }
        }

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

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            });
            return true;
        }
        catch (Exception ex)
        {
            LogClipboardDiagnostic($"Erro ao copiar para Windows Clipboard: {ex.Message}");
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
