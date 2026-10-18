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

    private static readonly Dictionary<int, string> CategoryMap = new()
    {
        // Telas, Computadores e Tecnologia
        { 508, "Computador / Teclado" },
        { 527, "Desktop / Gabinete" },
        { 620, "Notebook / Laptop" },
        { 664, "Monitor / Tela" },
        { 681, "Notebook / Computador" },
        { 782, "Tela / Interface" },
        { 851, "Televisão / Monitor" },
        { 916, "Página Web / Documento" },
        { 920, "Diagrama / Gráfico" },

        // Veículos
        { 407, "Ambulância" },
        { 436, "Carro de Praia" },
        { 479, "Carro / Automóvel" },
        { 511, "Conversível" },
        { 609, "Jipe / SUV" },
        { 656, "Minivan" },
        { 705, "Caminhão" },
        { 751, "Carro de Corrida" },
        { 817, "Carro Esportivo" },

        // Pessoas, Vestimentas e Dia a Dia
        { 834, "Traje / Pessoa" },
        { 846, "Rosto / Retrato" },
        { 981, "Foto / Retrato" },
        { 982, "Ambiente / Interior" },

        // Natureza e Animais
        { 985, "Natureza / Paisagem" },
        { 970, "Pico / Montanha" },
        { 972, "Praia / Litoral" },
        { 281, "Gato / Felino" },
        { 207, "Cachorro / Cão" }
    };

    private void EnsureSession()
    {
        if (_initialized || _isDisposed) return;
        _initialized = true;

        try
        {
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

            // 2. Montar Tensor Normalizado [1, 3, 224, 224]
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

            // 4. Obter Top-3 classes mais prováveis
            var scores = new List<(int Index, float Score)>();
            int idx = 0;
            foreach (var score in outputTensor)
            {
                scores.Add((idx++, score));
            }

            var topClasses = scores.OrderByDescending(s => s.Score).Take(3).ToList();
            var tags = new HashSet<string>();

            foreach (var item in topClasses)
            {
                if (CategoryMap.TryGetValue(item.Index, out var label))
                {
                    tags.Add(label);
                }
            }

            if (tags.Count == 0)
            {
                // Tags heurísticas visuais caso a classe exata seja rara
                tags.Add("Interface / Imagem");
            }

            return string.Join(", ", tags);
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
