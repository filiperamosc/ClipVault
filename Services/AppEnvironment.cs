using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ClipVault.Services;

public static class AppEnvironment
{
    public const string AppName = "ClipVault";
    public const string Version = "1.5.1";
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

    private const string IconResourceName = "ClipVault.AppIcon.clipvault.ico";
    private static readonly object _iconLock = new();
    private static string? _cachedIconPath;

    /// <summary>
    /// Bytes do ícone multirresolução (16→256px) incorporado no assembly.
    /// </summary>
    public static byte[]? GetEmbeddedIconBytes()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(IconResourceName)
                ?? (Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("clipvault.ico", StringComparison.OrdinalIgnoreCase)) is string name
                    ? assembly.GetManifestResourceStream(name)
                    : null);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Falha ao ler ícone incorporado: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Materializa o ícone incorporado em disco e retorna o caminho absoluto.
    /// A fonte da verdade é SEMPRE o recurso incorporado: arquivos "appicon.ico" soltos
    /// ao lado do executável (gerados pelo resizetizer do MAUI a partir do template .NET)
    /// são ignorados. O conteúdo é validado byte a byte e regravado se divergir.
    /// </summary>
    public static string EnsureIconExtracted()
    {
        lock (_iconLock)
        {
            if (_cachedIconPath != null && File.Exists(_cachedIconPath))
                return _cachedIconPath;

            var bytes = GetEmbeddedIconBytes();
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName),
                Path.Combine(Path.GetTempPath(), AppName)
            };

            foreach (var dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var target = Path.Combine(dir, $"clipvault-{Version}.ico");

                    bool upToDate = File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes);
                    if (!upToDate)
                    {
                        var tmp = target + ".tmp";
                        File.WriteAllBytes(tmp, bytes);
                        File.Move(tmp, target, overwrite: true);
                    }

                    _cachedIconPath = Path.GetFullPath(target);
                    return _cachedIconPath;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Falha ao gravar ícone em '{dir}': {ex.Message}");
                }
            }

            return string.Empty;
        }
    }
}
