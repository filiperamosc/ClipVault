using System;
using ClipVault.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipVault.ViewModels;

public partial class ClipboardItemViewModel : ObservableObject
{
    public ClipboardItem Model { get; }

    [ObservableProperty]
    private bool _isPinned;

    public int Id => Model.Id;
    public string Type => Model.Type;
    public string? Content => Model.Content;
    public string? PreviewText => Model.PreviewText;
    public string? FilePath => Model.FilePath;
    public string FormattedSize => Model.FormattedSize;
    public string FormattedDate => Model.FormattedDate;
    public bool IsImage => Model.IsImage;
    public bool IsText => Model.IsText;

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
    }

    public void UpdatePinnedState(bool pinned)
    {
        IsPinned = pinned;
        Model.IsPinned = pinned;
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinText));
    }
}
