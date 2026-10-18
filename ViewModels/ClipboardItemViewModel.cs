using System;
using ClipVault.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipVault.ViewModels;

public partial class ClipboardItemViewModel : ObservableObject
{
    public ClipboardItem Model { get; }

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private string? _extractedText;

    [ObservableProperty]
    private string? _imageTags;

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
    public string OcrPreview => HasExtractedText ? (ExtractedText!.Length > 60 ? ExtractedText.Substring(0, 60) + "..." : ExtractedText) : string.Empty;

    public string TypeIcon => Type switch
    {
        "Image" => "🖼️",
        "Url" => "🔗",
        "Code" => "💻",
        _ => "📝"
    };

    public string PinIcon => IsPinned ? "📌" : "📍";
    public string PinText => IsPinned ? "Desafixar" : "Fixar";

    public ClipboardItemViewModel(ClipboardItem model)
    {
        Model = model;
        _isPinned = model.IsPinned;
        _extractedText = model.ExtractedText;
        _imageTags = model.ImageTags;
    }

    public void UpdatePinnedState(bool pinned)
    {
        IsPinned = pinned;
        Model.IsPinned = pinned;
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinText));
    }

    public void UpdateAiMetadata(string? ocr, string? tags)
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
