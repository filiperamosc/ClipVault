using System;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;

namespace ClipVault.Services;

public class AutoCleanService : IAutoCleanService, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Timer? _timer;
    private bool _isDisposed;

    public AutoCleanService(IDatabaseService databaseService, ISettingsService settingsService)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
    }

    public void StartPeriodicCleaning()
    {
        if (_isDisposed) return;
        _timer?.Dispose();
        _timer = new Timer(async _ => await SafeExecuteCleanAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromHours(24));
    }

    public void StopPeriodicCleaning()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task SafeExecuteCleanAsync()
    {
        if (_isDisposed || _cts.IsCancellationRequested) return;

        try
        {
            await ExecuteCleanNowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro na limpeza automática periódica: {ex.Message}");
        }
    }

    public async Task<int> ExecuteCleanNowAsync()
    {
        if (_isDisposed || _cts.IsCancellationRequested) return 0;

        await _semaphore.WaitAsync(_cts.Token);
        try
        {
            if (_cts.IsCancellationRequested) return 0;

            var config = _settingsService.CurrentConfig;
            int totalRemoved = 0;

            // 1. Limpeza por Idade / Dias
            if (config.EnableAutoCleanByDays && config.AutoCleanDays > 0)
            {
                var removedByAge = await _databaseService.AutoCleanByAgeAsync(config.AutoCleanDays);
                totalRemoved += removedByAge;
            }

            if (_cts.IsCancellationRequested) return totalRemoved;

            // 2. Limpeza por Limite de Armazenamento
            if (config.EnableAutoCleanBySize && config.MaxStorageMb > 0)
            {
                long maxBytes = (long)config.MaxStorageMb * 1024 * 1024;
                var removedBySize = await _databaseService.AutoCleanBySizeAsync(maxBytes);
                totalRemoved += removedBySize;
            }

            return totalRemoved;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            _timer?.Dispose();
            _semaphore.Dispose();
            _cts.Dispose();
        }
        catch { }
    }
}
