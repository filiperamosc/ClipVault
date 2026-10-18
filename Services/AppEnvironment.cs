using System;
using System.Diagnostics;
using System.IO;

namespace ClipVault.Services;

public static class AppEnvironment
{
    public const string AppName = "ClipVault";
    public const string Version = "1.0.3";
    public const string ReleaseDate = "Outubro de 2026";
    public const string Author = "Filipe R. Carvalho";

    private static string? _appRootDirectory;

    public static string AppRootDirectory
    {
        get
        {
            if (_appRootDirectory != null) return _appRootDirectory;

            try
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    processPath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    _appRootDirectory = Path.GetDirectoryName(processPath);
                }
            }
            catch
            {
                // Fallback
            }

            if (string.IsNullOrWhiteSpace(_appRootDirectory))
            {
                _appRootDirectory = AppContext.BaseDirectory;
            }

            return _appRootDirectory ?? Directory.GetCurrentDirectory();
        }
    }

    public static string DatabasePath => Path.Combine(AppRootDirectory, "clipvault.db");
    public static string ConfigFilePath => Path.Combine(AppRootDirectory, "clipvault_config.json");
    public static string DefaultStorageDirectory => Path.Combine(AppRootDirectory, "Storage");

    public static void EnsureDirectoriesAndMigration()
    {
        try
        {
            var root = AppRootDirectory;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            // Migrar banco legado .db3 caso exista
            var legacyDb1 = Path.Combine(root, "clipvault.db3");
            var targetDb = DatabasePath;
            if (File.Exists(legacyDb1) && !File.Exists(targetDb))
            {
                File.Copy(legacyDb1, targetDb, overwrite: true);
            }

            var legacyDb2 = Path.Combine(DefaultStorageDirectory, "clipvault.db3");
            if (File.Exists(legacyDb2) && !File.Exists(targetDb))
            {
                File.Copy(legacyDb2, targetDb, overwrite: true);
            }

            if (!Directory.Exists(DefaultStorageDirectory))
            {
                Directory.CreateDirectory(DefaultStorageDirectory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao inicializar AppEnvironment: {ex.Message}");
        }
    }
}
