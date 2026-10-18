using System;
using ClipVault.Models;
using CommunityToolkit.Mvvm.ComponentModel;

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

    public int Id => Model.Id;
    public string Type => Model.Type;
    public string? Content => Model.Content;
    public string? PreviewText => Model.PreviewText;
    public string? FilePath => Model.FilePath;
    public string FormattedSize => Model.FormattedSize;
    public string FormattedDate => Model.FormattedDate;
    public bool IsImage => Model.IsImage;
    public bool IsText => Model.IsText;

    public bool HasExtractedText => !string.IsNullOrWhiteSpace(ExtractedText);
    public bool HasImageTags => !string.IsNullOrWhiteSpace(ImageTags);
    public string OcrPreview => HasExtractedText ? (ExtractedText!.Length > 55 ? ExtractedText.Substring(0, 55) + "..." : ExtractedText) : string.Empty;

    // Segoe Fluent Icons Glyphs
    public string TypeIcon => Type switch
    {
        "Image" => "\uEB9F", // Photo2
        "Url" => "\uE71B",   // Link
        "Code" => "\uE943",  // Code
        _ => "\uE8A5"        // Document / Text
    };

    public string TypeDisplayName => Type switch
    {
        "Image" => "Imagem",
        "Url" => "Link",
        "Code" => "Código",
        _ => "Texto"
    };

    public string PinIcon => IsPinned ? "\uE840" : "\uE718";
    public string PinText => IsPinned ? "Desafixar" : "Fixar";
    public string PinIconColor => IsPinned ? "#F59E0B" : "#A1A1AA";

    public ClipboardItemViewModel(ClipboardItem model)
    {
        Model = model;
        IsPinned = model.IsPinned;
        ExtractedText = model.ExtractedText;
        ImageTags = model.ImageTags;
    }

    public void UpdatePinnedState(bool pinned)
    {
        IsPinned = pinned;
        Model.IsPinned = pinned;
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinText));
        OnPropertyChanged(nameof(PinIconColor));
    }

    public void UpdateVisionMetadata(string? ocr, string? tags)
    {
        ExtractedText = ocr;
        ImageTags = tags;
        Model.ExtractedText = ocr;
        Model.ImageTags = tags;
        OnPropertyChanged(nameof(HasExtractedText));
        OnPropertyChanged(nameof(HasImageTags));
        OnPropertyChanged(nameof(OcrPreview));
    }
}
