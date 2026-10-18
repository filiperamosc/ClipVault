using System.Threading;
using System.Threading.Tasks;

namespace ClipVault.Services;

public interface IOcrService
{
    Task<string?> ExtractTextFromImageAsync(string imagePath, CancellationToken cancellationToken = default);
}
