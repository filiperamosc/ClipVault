using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
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
            if (_ocrEngine == null || cancellationToken.IsCancellationRequested) return null;

            using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var raStream = fileStream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(raStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            if (softwareBitmap.PixelWidth > OcrEngine.MaxImageDimension || softwareBitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                return null;
            }

            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            if (ocrResult != null && ocrResult.Lines != null && ocrResult.Lines.Count > 0)
            {
                var lines = ocrResult.Lines
                    .Select(l => l.Text?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                var fullText = string.Join(Environment.NewLine, lines);
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    return fullText.Trim();
                }
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
