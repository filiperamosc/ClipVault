using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Windows.Graphics.Imaging;
using Windows.Storage;
#endif

namespace ClipVault.Services;

public class VisionClassifierService : IVisionClassifierService
{
#if WINDOWS
    private InferenceSession? _session;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private bool _initialized;
    private bool _isDisposed;
    private string[]? _labels;

    private void EnsureLabels()
    {
        if (_labels != null && _labels.Length > 0) return;

        try
        {
            var path = Path.Combine(AppEnvironment.AppRootDirectory, "imagenet_pt.txt");
            if (File.Exists(path))
            {
                _labels = File.ReadAllLines(path, System.Text.Encoding.UTF8);
                return;
            }

            var localPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "imagenet_pt.txt");
            if (File.Exists(localPath))
            {
                _labels = File.ReadAllLines(localPath, System.Text.Encoding.UTF8);
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("ClipVault.Resources.Models.imagenet_pt.txt");
            if (stream != null)
            {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                var list = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    list.Add(line);
                }
                _labels = list.ToArray();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao carregar labels do ImageNet: {ex.Message}");
        }
    }

    private void EnsureSession()
    {
        if (_initialized || _isDisposed) return;
        _initialized = true;

        try
        {
            EnsureLabels();

            // 1. Tentar ler do diretório da aplicação
            var modelPath = Path.Combine(AppEnvironment.AppRootDirectory, "mobilenet_v2.onnx");
            if (File.Exists(modelPath))
            {
                _session = new InferenceSession(modelPath);
                return;
            }

            // 2. Tentar ler dos recursos do projeto
            var localModelPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "mobilenet_v2.onnx");
            if (File.Exists(localModelPath))
            {
                _session = new InferenceSession(localModelPath);
                return;
            }

            // 3. Tentar carregar de Embedded Resource
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("ClipVault.Resources.Models.mobilenet_v2.onnx");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _session = new InferenceSession(ms.ToArray());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao carregar modelo ONNX: {ex.Message}");
        }
    }

    public async Task<string?> ClassifyImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureSession();
            EnsureLabels();
            if (_session == null || cancellationToken.IsCancellationRequested) return null;

            // 1. Decodificar e redimensionar nativamente no Windows para 224x224
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var transform = new BitmapTransform
            {
                ScaledWidth = 224,
                ScaledHeight = 224,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            byte[] pixels = pixelData.DetachPixelData();
            if (pixels == null || pixels.Length < 224 * 224 * 4) return null;

            // 2. Montar Tensor Normalizado [1, 3, 224, 224] segundo padrão ImageNet
            int channelStride = 224 * 224;
            float[] tensorData = new float[3 * channelStride];
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            for (int y = 0; y < 224; y++)
            {
                for (int x = 0; x < 224; x++)
                {
                    int pixelIndex = (y * 224 + x) * 4;
                    float r = pixels[pixelIndex] / 255.0f;
                    float g = pixels[pixelIndex + 1] / 255.0f;
                    float b = pixels[pixelIndex + 2] / 255.0f;

                    tensorData[0 * channelStride + y * 224 + x] = (r - mean[0]) / std[0];
                    tensorData[1 * channelStride + y * 224 + x] = (g - mean[1]) / std[1];
                    tensorData[2 * channelStride + y * 224 + x] = (b - mean[2]) / std[2];
                }
            }

            if (cancellationToken.IsCancellationRequested) return null;

            // 3. Executar Inferência ONNX
            var inputName = _session.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(tensorData, new[] { 1, 3, 224, 224 });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            // 4. Calcular Softmax para obter probabilidades reais
            var logits = outputTensor.ToArray();
            float maxLogit = float.MinValue;
            for (int i = 0; i < logits.Length; i++)
            {
                if (logits[i] > maxLogit) maxLogit = logits[i];
            }

            float sumExp = 0f;
            float[] probs = new float[logits.Length];
            for (int i = 0; i < logits.Length; i++)
            {
                probs[i] = MathF.Exp(logits[i] - maxLogit);
                sumExp += probs[i];
            }

            if (sumExp > 0f)
            {
                for (int i = 0; i < probs.Length; i++)
                {
                    probs[i] /= sumExp;
                }
            }

            // 5. Extrair Top-5 classes e filtrar multi-labels por significância
            var ranked = probs.Select((prob, index) => (Index: index, Probability: prob))
                              .OrderByDescending(p => p.Probability)
                              .Take(5)
                              .ToList();

            var resultTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < ranked.Count; i++)
            {
                var (classIndex, probability) = ranked[i];

                // Critério multi-label:
                // - A predição Top 1 é sempre incluída
                // - Predições subsequentes (2 a 5) são incluídas se probabilidade >= 15% (ou >= 8% para top 3)
                bool isSignificant = (i == 0) || (probability >= 0.15f) || (i < 3 && probability >= 0.08f);

                if (isSignificant && _labels != null && classIndex >= 0 && classIndex < _labels.Length)
                {
                    var labelTokens = _labels[classIndex].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var token in labelTokens)
                    {
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            resultTags.Add(token);
                        }
                    }
                }
            }

            if (resultTags.Count == 0 && ranked.Count > 0 && _labels != null)
            {
                var topTokens = _labels[ranked[0].Index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in topTokens)
                {
                    resultTags.Add(token);
                }
            }

            return resultTags.Count > 0 ? string.Join(", ", resultTags) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro na classificação ONNX da imagem {imagePath}: {ex.Message}");
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _session?.Dispose();
            _session = null;
            _sessionLock.Dispose();
        }
        catch { }
    }
#else
    public Task<string?> ClassifyImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
#endif
}
