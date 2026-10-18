using System;
using SQLite;

namespace ClipVault.Models;

[Table("ClipboardItems")]
public class ClipboardItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Type { get; set; } = "Text"; // Text, Image, Url, Code

    public string? Content { get; set; }

    public string? PreviewText { get; set; }

    public string? FilePath { get; set; }

    public string? ThumbnailPath { get; set; }

    [Indexed]
    public string? ContentHash { get; set; }

    [Indexed]
    public string? ExtractedText { get; set; }

    [Indexed]
    public string? ImageTags { get; set; }

    [Indexed]
    public string? NormalizedSearchContent { get; set; }

    public long ByteSize { get; set; }

    [Indexed]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public bool IsPinned { get; set; }

    [Ignore]
    public string FormattedSize
    {
        get
        {
            if (ByteSize < 1024) return $"{ByteSize} B";
            if (ByteSize < 1024 * 1024) return $"{ByteSize / 1024.0:F1} KB";
            return $"{ByteSize / (1024.0 * 1024.0):F1} MB";
        }
    }

    [Ignore]
    public string FormattedDate => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    [Ignore]
    public bool IsImage => Type == "Image" && !string.IsNullOrEmpty(FilePath);

    [Ignore]
    public bool IsText => Type != "Image";
}
