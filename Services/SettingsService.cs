using System;
using System.IO;
using System.Text.Json;
using ClipVault.Models;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace ClipVault.Services;

public class SettingsService : ISettingsService
{
    private readonly string _configFilePath;
    private AppConfig _currentConfig;

    public AppConfig CurrentConfig => _currentConfig;

    public SettingsService()
    {
        AppEnvironment.EnsureDirectoriesAndMigration();
        _configFilePath = AppEnvironment.ConfigFilePath;
        _currentConfig = LoadConfig();
    }

    private AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    if (string.IsNullOrWhiteSpace(config.StorageDirectory))
                    {
                        config.StorageDirectory = AppEnvironment.DefaultStorageDirectory;
                    }
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao ler configuração: {ex.Message}");
        }

        var defaultConfig = new AppConfig
        {
            StorageDirectory = AppEnvironment.DefaultStorageDirectory
        };
        SaveConfig(defaultConfig);
        return defaultConfig;
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            _currentConfig = config;
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao salvar configurações: {ex.Message}");
        }
    }

    public string GetEffectiveStorageDirectory()
    {
        var dir = _currentConfig.StorageDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = AppEnvironment.DefaultStorageDirectory;
        }

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return dir;
    }

    public void SetStartWithWindows(bool enable)
    {
#if WINDOWS
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string appName = "ClipVault";
            using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(appName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
            _currentConfig.StartWithWindows = enable;
            SaveConfig(_currentConfig);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao configurar inicialização no Windows: {ex.Message}");
        }
#endif
    }

    public bool CheckStartWithWindows()
    {
#if WINDOWS
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string appName = "ClipVault";
            using var key = Registry.CurrentUser.OpenSubKey(runKey, false);
            return key?.GetValue(appName) != null;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }
}
