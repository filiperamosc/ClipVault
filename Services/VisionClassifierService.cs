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
#endif

namespace ClipVault.Services;

public class VisionClassifierService : IVisionClassifierService
{
#if WINDOWS
    private InferenceSession? _session;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly object _logLock = new();
    private bool _initialized;
    private bool _isDisposed;
    private string[]? _labels;

    // Limiar mínimo de confiança ajustado para 40% (0.40f)
    private const float MinConfidenceThreshold = 0.40f;
    private const int ModelInputSize = 224;

    private void LogDiagnostic(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);

            lock (_logLock)
            {
                var logPath = Path.Combine(AppEnvironment.AppRootDirectory, "vision_diagnostics.log");
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
            else
            {
                LogDiagnostic("ERRO: Recurso embutido 'ClipVault.Resources.Models.imagenet_pt.txt' não encontrado.");
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"ERRO ao carregar labels do ImageNet: {ex.Message}");
        }
    }

    private void EnsureSession()
    {
        if (_initialized || _isDisposed) return;
        _initialized = true;

        try
        {
            EnsureLabels();

            var modelPath = Path.Combine(AppEnvironment.AppRootDirectory, "mobilenet_v2.onnx");
            if (File.Exists(modelPath))
            {
                _session = new InferenceSession(modelPath);
                return;
            }

            var localModelPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "mobilenet_v2.onnx");
            if (File.Exists(localModelPath))
            {
                _session = new InferenceSession(localModelPath);
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("ClipVault.Resources.Models.mobilenet_v2.onnx");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _session = new InferenceSession(ms.ToArray());
            }
            else
            {
                LogDiagnostic("ERRO: Recurso embutido 'ClipVault.Resources.Models.mobilenet_v2.onnx' não encontrado.");
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"ERRO ao inicializar sessão ONNX: {ex.Message}");
        }
    }

    public async Task<string?> ClassifyImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            LogDiagnostic($"Aviso: Caminho de imagem inválido ou inexistente: '{imagePath}'");
            return null;
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureSession();
            EnsureLabels();
            if (_session == null || _labels == null || _labels.Length == 0 || cancellationToken.IsCancellationRequested)
            {
                LogDiagnostic("ERRO: Sessão ONNX ou arquivo de labels não disponível para inferência.");
                return null;
            }

            // 1. Abrir arquivo via FileStream compartilhado e decodificar pixels RGBA8 brutos sem falha de perfil de cor
            byte[] srcPixels;
            int srcW;
            int srcH;

            using (var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var raStream = fileStream.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(raStream);
                srcW = (int)decoder.PixelWidth;
                srcH = (int)decoder.PixelHeight;

                if (srcW <= 0 || srcH <= 0)
                {
                    LogDiagnostic($"ERRO: Dimensões inválidas ({srcW}x{srcH}) na imagem '{Path.GetFileName(imagePath)}'.");
                    return null;
                }

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                srcPixels = pixelData.DetachPixelData();
            }

            if (srcPixels == null || srcPixels.Length < srcW * srcH * 4)
            {
                LogDiagnostic($"ERRO: Buffer de pixels insuficiente ({srcPixels?.Length ?? 0} bytes) para {srcW}x{srcH}.");
                return null;
            }

            if (cancellationToken.IsCancellationRequested) return null;

            // 2. Pré-processamento 224x224 preservando a proporção:
            //    - Tensor A: Letterbox (escala proporcional com preenchimento neutro 224x224, preservando 100% do objeto)
            //    - Tensor B: Center Crop (escala proporcional do menor lado para 256 e recorte central 224x224)
            float[] letterboxTensor = BuildLetterboxTensor(srcPixels, srcW, srcH);
            float[] centerCropTensor = BuildCenterCropTensor(srcPixels, srcW, srcH);

            float[] probsLetterbox = RunInferenceSoftmax(letterboxTensor);
            float[] probsCenterCrop = RunInferenceSoftmax(centerCropTensor);

            int numClasses = Math.Min(probsLetterbox.Length, probsCenterCrop.Length);
            if (numClasses == 0)
            {
                LogDiagnostic("ERRO: Tensor de saída vazio após inferência ONNX.");
                return null;
            }

            // Combinar probabilidades (máximo elemento a elemento entre Letterbox e Center Crop)
            float[] combinedProbs = new float[numClasses];
            for (int i = 0; i < numClasses; i++)
            {
                combinedProbs[i] = MathF.Max(probsLetterbox[i], probsCenterCrop[i]);
            }

            // O modelo google/mobilenet_v2_1.0_224 possui 1001 saídas (índice 0 = background, 1..1000 = ImageNet 0..999)
            bool hasBackgroundOffset = numClasses == 1001;

            var topCandidates = combinedProbs
                .Select((prob, idx) => (ModelIndex: idx, LabelIndex: hasBackgroundOffset ? idx - 1 : idx, Probability: prob))
                .Where(p => p.LabelIndex >= 0 && p.LabelIndex < _labels.Length)
                .OrderByDescending(p => p.Probability)
                .Take(5)
                .ToList();

            if (topCandidates.Count == 0)
            {
                LogDiagnostic($"Aviso: Nenhuma classe candidata para '{Path.GetFileName(imagePath)}'.");
                return null;
            }

            var topSummary = string.Join(" | ", topCandidates.Take(3).Select(c => $"[{c.LabelIndex}] {_labels[c.LabelIndex].Split(',')[0]}={c.Probability:P1}"));

            // 3. Seleção de Tags com Limiar de Confiança de 40% (0.40f):
            //    a) Classes individuais com probabilidade >= 40%
            //    b) Agregação semântica entre as Top-5 classes (quando o modelo divide a probabilidade entre raças/subtipos do mesmo animal ou objeto, ex: 25% + 20% = 45% >= 40%)
            var resultTags = new List<string>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddTagsFromLabel(int labelIdx)
            {
                var tokens = _labels[labelIdx].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in tokens)
                {
                    if (!string.IsNullOrWhiteSpace(token) && seenTags.Add(token))
                    {
                        resultTags.Add(token);
                    }
                }
            }

            foreach (var candidate in topCandidates.Where(c => c.Probability >= MinConfidenceThreshold).Take(3))
            {
                AddTagsFromLabel(candidate.LabelIndex);
            }

            // Se nenhuma classe individual atingiu 40%, verificar se a soma das probabilidades das Top-5 classes
            // para uma mesma categoria semântica (ex: "cavalo", "cachorro", "gato", "arvore", "carro") atinge >= 40%
            if (resultTags.Count == 0 && topCandidates[0].Probability >= 0.16f)
            {
                var tagProbSums = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in topCandidates)
                {
                    var uniqueTokens = _labels[candidate.LabelIndex]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var token in uniqueTokens)
                    {
                        tagProbSums[token] = tagProbSums.GetValueOrDefault(token) + candidate.Probability;
                    }
                }

                var qualifiedSemanticTags = tagProbSums
                    .Where(kv => kv.Value >= MinConfidenceThreshold)
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                if (qualifiedSemanticTags.Count > 0)
                {
                    // Inclui os rótulos da classe principal e os rótulos semânticos que superaram 40%
                    AddTagsFromLabel(topCandidates[0].LabelIndex);
                    foreach (var tag in qualifiedSemanticTags)
                    {
                        if (seenTags.Add(tag))
                        {
                            resultTags.Add(tag);
                        }
                    }
                }
            }

            if (resultTags.Count == 0)
            {
                LogDiagnostic($"Descartado (< 40%): '{Path.GetFileName(imagePath)}' ({srcW}x{srcH}) -> {topSummary}");
                return null;
            }

            var finalTags = string.Join(", ", resultTags.Take(6));
            LogDiagnostic($"Classificado (>= 40%): '{Path.GetFileName(imagePath)}' ({srcW}x{srcH}) -> '{finalTags}' ({topSummary})");
            return finalTags;
        }
        catch (Exception ex)
        {
            LogDiagnostic($"FALHA na classificação ONNX de '{imagePath}': {ex.GetType().Name} - {ex.Message}");
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private float[] RunInferenceSoftmax(float[] tensorData)
    {
        if (_session == null) return Array.Empty<float>();

        var inputName = _session.InputMetadata.Keys.First();
        var inputTensor = new DenseTensor<float>(tensorData, new[] { 1, 3, ModelInputSize, ModelInputSize });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

        using var results = _session.Run(inputs);
        var logits = results.First().AsTensor<float>().ToArray();
        if (logits.Length == 0) return Array.Empty<float>();

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

        return probs;
    }

    /// <summary>
    /// Redimensiona a imagem para 224x224 preservando a proporção via Letterbox (barras neutras) com interpolação bilinear.
    /// </summary>
    private static float[] BuildLetterboxTensor(byte[] srcPixels, int srcW, int srcH)
    {
        int stride = ModelInputSize * ModelInputSize;
        float[] tensor = new float[3 * stride];

        double scale = (double)ModelInputSize / Math.Max(srcW, srcH);
        int newW = Math.Max(1, (int)Math.Round(srcW * scale));
        int newH = Math.Max(1, (int)Math.Round(srcH * scale));
        int padX = (ModelInputSize - newW) / 2;
        int padY = (ModelInputSize - newH) / 2;

        for (int y = 0; y < ModelInputSize; y++)
        {
            for (int x = 0; x < ModelInputSize; x++)
            {
                int dstIdx = y * ModelInputSize + x;

                if (x >= padX && x < padX + newW && y >= padY && y < padY + newH)
                {
                    float u = (float)(x - padX) / newW * (srcW - 1);
                    float v = (float)(y - padY) / newH * (srcH - 1);
                    SampleBilinear(srcPixels, srcW, srcH, u, v, out float r, out float g, out float b);

                    tensor[0 * stride + dstIdx] = (r / 255.0f - 0.5f) / 0.5f;
                    tensor[1 * stride + dstIdx] = (g / 255.0f - 0.5f) / 0.5f;
                    tensor[2 * stride + dstIdx] = (b / 255.0f - 0.5f) / 0.5f;
                }
                else
                {
                    // Padding neutro (cinza médio -> 0.0f após normalização [-1, 1])
                    tensor[0 * stride + dstIdx] = 0.0f;
                    tensor[1 * stride + dstIdx] = 0.0f;
                    tensor[2 * stride + dstIdx] = 0.0f;
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// Redimensiona o menor lado para 256px preservando a proporção e extrai o recorte central 224x224 com interpolação bilinear.
    /// </summary>
    private static float[] BuildCenterCropTensor(byte[] srcPixels, int srcW, int srcH)
    {
        int stride = ModelInputSize * ModelInputSize;
        float[] tensor = new float[3 * stride];

        const double resizeShortest = 256.0;
        double scale = resizeShortest / Math.Min(srcW, srcH);
        double scaledW = Math.Max(ModelInputSize, srcW * scale);
        double scaledH = Math.Max(ModelInputSize, srcH * scale);
        double offsetX = (scaledW - ModelInputSize) * 0.5;
        double offsetY = (scaledH - ModelInputSize) * 0.5;

        for (int y = 0; y < ModelInputSize; y++)
        {
            for (int x = 0; x < ModelInputSize; x++)
            {
                float u = (float)((x + offsetX) / scaledW * (srcW - 1));
                float v = (float)((y + offsetY) / scaledH * (srcH - 1));
                SampleBilinear(srcPixels, srcW, srcH, u, v, out float r, out float g, out float b);

                int dstIdx = y * ModelInputSize + x;
                tensor[0 * stride + dstIdx] = (r / 255.0f - 0.5f) / 0.5f;
                tensor[1 * stride + dstIdx] = (g / 255.0f - 0.5f) / 0.5f;
                tensor[2 * stride + dstIdx] = (b / 255.0f - 0.5f) / 0.5f;
            }
        }

        return tensor;
    }

    private static void SampleBilinear(byte[] src, int w, int h, float u, float v, out float r, out float g, out float b)
    {
        u = Math.Clamp(u, 0f, Math.Max(0, w - 1));
        v = Math.Clamp(v, 0f, Math.Max(0, h - 1));

        int x0 = (int)u;
        int y0 = (int)v;
        int x1 = Math.Min(x0 + 1, w - 1);
        int y1 = Math.Min(y0 + 1, h - 1);

        float dx = u - x0;
        float dy = v - y0;

        int i00 = (y0 * w + x0) * 4;
        int i10 = (y0 * w + x1) * 4;
        int i01 = (y1 * w + x0) * 4;
        int i11 = (y1 * w + x1) * 4;

        float w00 = (1f - dx) * (1f - dy);
        float w10 = dx * (1f - dy);
        float w01 = (1f - dx) * dy;
        float w11 = dx * dy;

        r = src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11;
        g = src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11;
        b = src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11;
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
