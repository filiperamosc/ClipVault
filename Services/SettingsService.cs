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
        try
        {
            AppEnvironment.EnsureDirectoriesAndMigration();
            _configFilePath = AppEnvironment.ConfigFilePath;
            _currentConfig = LoadConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao inicializar SettingsService: {ex.Message}");
            _configFilePath = Path.Combine(Path.GetTempPath(), "clipvault_config.json");
            _currentConfig = new AppConfig
            {
                StorageDirectory = AppEnvironment.DefaultStorageDirectory,
                EnableAutoCleanByDays = true,
                AutoCleanDays = 15,
                EnableAutoCleanBySize = true,
                MaxStorageMb = 500,
                StartWithWindows = false,
                IsMonitoringPaused = false
            };
        }
    }

    private AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var config = JsonSerializer.Deserialize<AppConfig>(json, options);
                    if (config != null)
                    {
                        // Validação e sanitização contra valores nulos ou quebrados de versões legadas
                        if (string.IsNullOrWhiteSpace(config.StorageDirectory))
                        {
                            config.StorageDirectory = AppEnvironment.DefaultStorageDirectory;
                        }

                        if (config.AutoCleanDays <= 0)
                        {
                            config.AutoCleanDays = 15;
                        }

                        if (config.MaxStorageMb <= 0)
                        {
                            config.MaxStorageMb = 500;
                        }

                        return config;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao ler configuração: {ex.Message}. Efetuando fallback seguro.");
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var backup = _configFilePath + $".corrupt.{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
                    File.Copy(_configFilePath, backup, overwrite: true);
                }
            }
            catch { }
        }

        var defaultConfig = new AppConfig
        {
            StorageDirectory = AppEnvironment.DefaultStorageDirectory,
            EnableAutoCleanByDays = true,
            AutoCleanDays = 15,
            EnableAutoCleanBySize = true,
            MaxStorageMb = 500,
            StartWithWindows = false,
            IsMonitoringPaused = false
        };

        try
        {
            SaveConfig(defaultConfig);
        }
        catch { }

        return defaultConfig;
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            _currentConfig = config;
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var tempFile = _configFilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _configFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao salvar configurações: {ex.Message}");
            try
            {
                // Fallback para gravação direta
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
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
