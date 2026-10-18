using System;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;

namespace ClipVault.Services;

public class AutoCleanService : IAutoCleanService, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private Timer? _timer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public AutoCleanService(IDatabaseService databaseService, ISettingsService settingsService)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
    }

    public void StartPeriodicCleaning()
    {
        // Dispara uma vez após 10 segundos da inicialização e depois a cada 24 horas
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
        await _semaphore.WaitAsync();
        try
        {
            var config = _settingsService.CurrentConfig;
            int totalRemoved = 0;

            // 1. Limpeza por Idade / Dias
            if (config.EnableAutoCleanByDays && config.AutoCleanDays > 0)
            {
                var removedByAge = await _databaseService.AutoCleanByAgeAsync(config.AutoCleanDays);
                totalRemoved += removedByAge;
            }

            // 2. Limpeza por Tamanho de Armazenamento
            if (config.EnableAutoCleanBySize && config.MaxStorageMb > 0)
            {
                long maxBytes = (long)config.MaxStorageMb * 1024 * 1024;
                var removedBySize = await _databaseService.AutoCleanBySizeAsync(maxBytes);
                totalRemoved += removedBySize;
            }

            return totalRemoved;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _semaphore.Dispose();
    }
}
