using System;
using System.IO;

namespace ClipVault.Models;

public class AppConfig
{
    public string StorageDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipVault",
        "Storage");

    public bool EnableAutoCleanByDays { get; set; } = true;
    public int AutoCleanDays { get; set; } = 15; // Dias para retenção

    public bool EnableAutoCleanBySize { get; set; } = true;
    public int MaxStorageMb { get; set; } = 500; // Limite em MB

    public bool StartWithWindows { get; set; } = false;
    public bool IsMonitoringPaused { get; set; } = false;
}
