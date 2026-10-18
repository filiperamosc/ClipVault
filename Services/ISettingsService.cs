using System.Threading.Tasks;
using ClipVault.Models;

namespace ClipVault.Services;

public interface ISettingsService
{
    AppConfig CurrentConfig { get; }
    void SaveConfig(AppConfig config);
    string GetEffectiveStorageDirectory();
    void SetStartWithWindows(bool enable);
    bool CheckStartWithWindows();
}
