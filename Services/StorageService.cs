using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ClipVault.Services;

public class StorageService : IStorageService
{
    private readonly ISettingsService _settingsService;

    public StorageService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ComputeTextHash(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        return ComputeBytesHash(bytes);
    }

    public string ComputeBytesHash(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task<string> SaveImageAsync(byte[] imageBytes, string extension = ".png")
    {
        var targetDir = _settingsService.GetEffectiveStorageDirectory();
        var imagesDir = Path.Combine(targetDir, "Images");
        if (!Directory.Exists(imagesDir))
        {
            Directory.CreateDirectory(imagesDir);
        }

        var fileName = $"clip_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}{extension}";
        var fullPath = Path.Combine(imagesDir, fileName);

        await File.WriteAllBytesAsync(fullPath, imageBytes);
        return fullPath;
    }

    public void DeleteFileIfExists(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao excluir arquivo {filePath}: {ex.Message}");
            }
        }
    }

    public long GetTotalStorageSize()
    {
        try
        {
            var dir = _settingsService.GetEffectiveStorageDirectory();
            if (!Directory.Exists(dir)) return 0;

            var dirInfo = new DirectoryInfo(dir);
            long total = 0;
            foreach (var file in dirInfo.GetFiles("*.*", SearchOption.AllDirectories))
            {
                total += file.Length;
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
