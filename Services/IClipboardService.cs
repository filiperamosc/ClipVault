using System;
using System.Threading.Tasks;
using ClipVault.Models;

namespace ClipVault.Services;

public interface IClipboardService : IDisposable
{
    event EventHandler<ClipboardItem>? ItemCaptured;
    bool IsMonitoringPaused { get; set; }
    void Start();
    void Stop();
    Task<bool> CopyToClipboardAsync(ClipboardItem item);
}
