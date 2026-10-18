using System;
using System.Globalization;
using System.Text;

namespace ClipVault.Services;

public static class TextNormalizationHelper
{
    /// <summary>
    /// Remove acentos e diacríticos (ex: "árvore" -> "arvore", "coração" -> "coracao")
    /// e converte para minúsculas para buscas insensíveis a acentos e maiúsculas/minúsculas.
    /// </summary>
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            var c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Gera o vetor de busca normalizado agregando conteúdo, preview, OCR e tags visuais.
    /// </summary>
    public static string BuildSearchVector(string? content, string? preview, string? ocr, string? tags)
    {
        var raw = $"{content} {preview} {ocr} {tags}";
        return RemoveDiacritics(raw);
    }
}
