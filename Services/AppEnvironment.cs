using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ClipVault.Services;

public static class AppEnvironment
{
    public const string AppName = "ClipVault";
    public const string Version = "1.1.3";
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

            EnsureIconExtracted();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao inicializar AppEnvironment: {ex.Message}");
        }
    }

    public static string EnsureIconExtracted()
    {
        try
        {
            var iconPath = Path.Combine(AppRootDirectory, "appicon.ico");
            if (File.Exists(iconPath) && new FileInfo(iconPath).Length > 0)
                return iconPath;

            var localIcon = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon", "appicon.ico");
            if (File.Exists(localIcon) && new FileInfo(localIcon).Length > 0)
            {
                File.Copy(localIcon, iconPath, overwrite: true);
                return iconPath;
            }

            var baseIcon = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(baseIcon) && new FileInfo(baseIcon).Length > 0)
            {
                File.Copy(baseIcon, iconPath, overwrite: true);
                return iconPath;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("appicon.ico", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fs = File.Create(iconPath);
                    stream.CopyTo(fs);
                    fs.Flush();
                    return iconPath;
                }
            }

            return iconPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao extrair ícone: {ex.Message}");
            return Path.Combine(AppRootDirectory, "appicon.ico");
        }
    }
}
