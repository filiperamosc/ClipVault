using System.IO;
using System.Threading.Tasks;

namespace ClipVault.Services;

public interface IStorageService
{
    string ComputeTextHash(string text);
    string ComputeBytesHash(byte[] bytes);
    Task<string> SaveImageAsync(byte[] imageBytes, string extension = ".png");
    void DeleteFileIfExists(string? filePath);
    long GetTotalStorageSize();
}
