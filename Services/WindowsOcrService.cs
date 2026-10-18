using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
#endif

namespace ClipVault.Services;

public class WindowsOcrService : IOcrService
{
#if WINDOWS
    private OcrEngine? _ocrEngine;
    private bool _initialized;

    private void EnsureEngine()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_ocrEngine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao inicializar OcrEngine do Windows: {ex.Message}");
        }
    }

    public async Task<string?> ExtractTextFromImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;

        try
        {
            EnsureEngine();
            if (_ocrEngine == null) return null;

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            if (ocrResult != null && !string.IsNullOrWhiteSpace(ocrResult.Text))
            {
                return ocrResult.Text.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao extrair OCR da imagem {imagePath}: {ex.Message}");
        }

        return null;
    }
#else
    public Task<string?> ExtractTextFromImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
#endif
}
