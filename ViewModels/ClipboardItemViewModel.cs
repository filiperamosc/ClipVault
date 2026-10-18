using System;
using System.IO;
using ClipVault.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;

namespace ClipVault.ViewModels;

public partial class ClipboardItemViewModel : ObservableObject
{
    public ClipboardItem Model { get; }

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    [ObservableProperty]
    public partial string? ExtractedText { get; set; }

    [ObservableProperty]
    public partial string? ImageTags { get; set; }

    public int Id => Model?.Id ?? 0;
    public string Type => Model?.Type ?? "Text";
    public string Content => Model?.Content ?? string.Empty;
    public string PreviewText => !string.IsNullOrEmpty(Model?.PreviewText)
        ? Model.PreviewText
        : (!string.IsNullOrEmpty(Model?.Content) ? Model.Content : string.Empty);

    public string FilePath => Model?.FilePath ?? string.Empty;
    public string FormattedSize => Model?.FormattedSize ?? "0 B";
    public string FormattedDate => Model != null ? Model.FormattedDate : string.Empty;

    /// <summary>
    /// Validação estrita: só é imagem se o tipo for Image E o arquivo existir fisicamente no disco.
    /// Evita crash no carregamento de imagens de registros antigos ou arquivos apagados.
    /// </summary>
    public bool IsImage
    {
        get
        {
            try
            {
                return Model != null &&
                       string.Equals(Model.Type, "Image", StringComparison.OrdinalIgnoreCase) &&
                       !string.IsNullOrWhiteSpace(Model.FilePath) &&
                       File.Exists(Model.FilePath);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsText => !IsImage;

    /// <summary>
    /// Retorna uma fonte segura de imagem para evitar crash de renderização no WinUI 3.
    /// </summary>
    public ImageSource? SafeImageSource
    {
        get
        {
            try
            {
                if (IsImage && !string.IsNullOrWhiteSpace(FilePath))
                {
                    return ImageSource.FromFile(FilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafeImageSource erro: {ex.Message}");
            }
            return null;
        }
    }

    public bool HasExtractedText => !string.IsNullOrWhiteSpace(ExtractedText);
    public bool HasImageTags => !string.IsNullOrWhiteSpace(ImageTags);
    public string SafeImageTags => ImageTags ?? string.Empty;

    public string OcrPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExtractedText)) return string.Empty;
            string clean = ExtractedText.Trim();
            return clean.Length > 55 ? clean.Substring(0, 55) + "..." : clean;
        }
    }

    // Segoe Fluent Icons Glyphs com fallback seguro
    public string TypeIcon => (Type ?? "Text") switch
    {
        "Image" => "\uEB9F", // Photo2
        "Url" => "\uE71B",   // Link
        "Code" => "\uE943",  // Code
        _ => "\uE8A5"        // Document / Text
    };

    public string TypeDisplayName => (Type ?? "Text") switch
    {
        "Image" => "Imagem",
        "Url" => "Link",
        "Code" => "Código",
        _ => "Texto"
    };

    public string PinIcon => IsPinned ? "\uE840" : "\uE718";
    public string PinText => IsPinned ? "Desafixar" : "Fixar";
    public string PinIconColor => IsPinned ? "#F59E0B" : "#A1A1AA";

    public ClipboardItemViewModel(ClipboardItem? model)
    {
        Model = model ?? new ClipboardItem { Type = "Text", Content = string.Empty };
        IsPinned = Model.IsPinned;
        ExtractedText = Model.ExtractedText ?? string.Empty;
        ImageTags = Model.ImageTags ?? string.Empty;
    }

    public void UpdatePinnedState(bool pinned)
    {
        IsPinned = pinned;
        if (Model != null)
        {
            Model.IsPinned = pinned;
        }
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinText));
        OnPropertyChanged(nameof(PinIconColor));
    }

    public void UpdateVisionMetadata(string? ocr, string? tags)
    {
        ExtractedText = ocr ?? string.Empty;
        ImageTags = tags ?? string.Empty;
        if (Model != null)
        {
            Model.ExtractedText = ocr;
            Model.ImageTags = tags;
        }
        OnPropertyChanged(nameof(HasExtractedText));
        OnPropertyChanged(nameof(HasImageTags));
        OnPropertyChanged(nameof(SafeImageTags));
        OnPropertyChanged(nameof(OcrPreview));
    }
}
