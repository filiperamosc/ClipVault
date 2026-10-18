using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ClipVault.Services;

public static class AppEnvironment
{
    public const string AppName = "ClipVault";
    public const string Version = "1.5";
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

            // Testar se o diretório tem permissão de escrita; caso contrário, redirecionar para LocalApplicationData
            try
            {
                if (!string.IsNullOrWhiteSpace(_appRootDirectory) && Directory.Exists(_appRootDirectory))
                {
                    var testFile = Path.Combine(_appRootDirectory, $".write_test_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
            }
            catch
            {
                _appRootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipVault");
                try { Directory.CreateDirectory(_appRootDirectory); } catch { }
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

            // Migrar banco legado .db3 caso exista de forma segura
            var targetDb = DatabasePath;
            if (!File.Exists(targetDb))
            {
                var legacyDb1 = Path.Combine(root, "clipvault.db3");
                if (File.Exists(legacyDb1))
                {
                    try { File.Copy(legacyDb1, targetDb, overwrite: false); } catch { }
                }

                if (!File.Exists(targetDb))
                {
                    var legacyDb2 = Path.Combine(DefaultStorageDirectory, "clipvault.db3");
                    if (File.Exists(legacyDb2))
                    {
                        try { File.Copy(legacyDb2, targetDb, overwrite: false); } catch { }
                    }
                }
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
            // 1. Verificar se o ícone já existe no diretório raiz da aplicação
            var rootIcon = Path.Combine(AppRootDirectory, "appicon.ico");
            if (File.Exists(rootIcon) && new FileInfo(rootIcon).Length > 0)
                return rootIcon;

            // 2. Verificar no diretório base de execução (bin/publish)
            var baseIcon = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(baseIcon) && new FileInfo(baseIcon).Length > 0)
                return baseIcon;

            // 3. Verificar na pasta Resources/AppIcon
            var localIcon = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon", "appicon.ico");
            if (File.Exists(localIcon) && new FileInfo(localIcon).Length > 0)
                return localIcon;

            // 4. Verificar no LocalApplicationData
            var localAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipVault");
            var localAppDataIcon = Path.Combine(localAppDataDir, "appicon.ico");
            if (File.Exists(localAppDataIcon) && new FileInfo(localAppDataIcon).Length > 0)
                return localAppDataIcon;

            // 5. Extrair do EmbeddedResource incorporado no binário
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("appicon.ico", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    // Tentar salvar no AppRootDirectory
                    try
                    {
                        using var fs = File.Create(rootIcon);
                        stream.CopyTo(fs);
                        fs.Flush();
                        return rootIcon;
                    }
                    catch
                    {
                        // Fallback para LocalApplicationData
                        try
                        {
                            Directory.CreateDirectory(localAppDataDir);
                            stream.Position = 0;
                            using var fsLocal = File.Create(localAppDataIcon);
                            stream.CopyTo(fsLocal);
                            fsLocal.Flush();
                            return localAppDataIcon;
                        }
                        catch
                        {
                            // Fallback para diretório temporário do Windows
                            var tempDir = Path.Combine(Path.GetTempPath(), "ClipVault");
                            Directory.CreateDirectory(tempDir);
                            var tempIcon = Path.Combine(tempDir, "appicon.ico");
                            stream.Position = 0;
                            using var fsTemp = File.Create(tempIcon);
                            stream.CopyTo(fsTemp);
                            fsTemp.Flush();
                            return tempIcon;
                        }
                    }
                }
            }

            return rootIcon;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao extrair ícone: {ex.Message}");
            return Path.Combine(AppRootDirectory, "appicon.ico");
        }
    }
}
