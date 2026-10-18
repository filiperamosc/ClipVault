using System.Threading.Tasks;

namespace ClipVault.Services;

public interface IAutoCleanService
{
    void StartPeriodicCleaning();
    void StopPeriodicCleaning();
    Task<int> ExecuteCleanNowAsync();
}
