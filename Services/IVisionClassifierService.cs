using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClipVault.Services;

public interface IVisionClassifierService : IDisposable
{
    Task<string?> ClassifyImageAsync(string imagePath, CancellationToken cancellationToken = default);
}
