using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipVault.Models;
using SQLite;

namespace ClipVault.Services;

public class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _isInitialized;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;

    public DatabaseService(
        ISettingsService settingsService,
        IStorageService storageService)
    {
        _settingsService = settingsService;
        _storageService = storageService;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized && _db != null) return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized && _db != null) return;

            await Task.Run(async () =>
            {
                AppEnvironment.EnsureDirectoriesAndMigration();
                var dbPath = AppEnvironment.DatabasePath;

                bool connectionSuccess = false;
                try
                {
                    var connection = new SQLiteAsyncConnection(
                        dbPath,
                        SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);

                    await connection.CreateTableAsync<ClipboardItem>().ConfigureAwait(false);
                    await EnsureSchemaColumnsAsync(connection).ConfigureAwait(false);

                    _db = connection;
                    _isInitialized = true;
                    connectionSuccess = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Falha ao inicializar SQLite: {ex.Message}. Tentando recuperação e fallback limpo...");
                }

                if (!connectionSuccess)
                {
                    try
                    {
                        if (_db != null)
                        {
                            try { await _db.CloseAsync().ConfigureAwait(false); } catch { }
                            _db = null;
                        }

                        if (File.Exists(dbPath))
                        {
                            var backupPath = dbPath + $".corrupt_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
                            try { File.Move(dbPath, backupPath, overwrite: true); } catch { }

                            var walPath = dbPath + "-wal";
                            if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }

                            var shmPath = dbPath + "-shm";
                            if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }
                        }

                        var freshConnection = new SQLiteAsyncConnection(
                            dbPath,
                            SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);

                        await freshConnection.CreateTableAsync<ClipboardItem>().ConfigureAwait(false);
                        _db = freshConnection;
                        _isInitialized = true;
                    }
                    catch (Exception fatalEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Falha catastrófica ao recriar SQLite limpo: {fatalEx.Message}");
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private class ColumnInfo
    {
        public string? name { get; set; }
    }

    private static async Task EnsureSchemaColumnsAsync(SQLiteAsyncConnection connection)
    {
        try
        {
            var columns = await connection.QueryAsync<ColumnInfo>("PRAGMA table_info(ClipboardItems);").ConfigureAwait(false);
            var existingNames = new HashSet<string>(columns.Select(c => c.name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            var columnsToVerify = new (string Name, string TypeSql)[]
            {
                ("Type", "TEXT DEFAULT 'Text'"),
                ("Content", "TEXT"),
                ("PreviewText", "TEXT"),
                ("FilePath", "TEXT"),
                ("ThumbnailPath", "TEXT"),
                ("ContentHash", "TEXT"),
                ("ExtractedText", "TEXT"),
                ("ImageTags", "TEXT"),
                ("NormalizedSearchContent", "TEXT"),
                ("ByteSize", "INTEGER DEFAULT 0"),
                ("CreatedAt", "DATETIME"),
                ("IsPinned", "INTEGER DEFAULT 0")
            };

            foreach (var (colName, colType) in columnsToVerify)
            {
                if (!existingNames.Contains(colName))
                {
                    try
                    {
                        await connection.ExecuteAsync($"ALTER TABLE ClipboardItems ADD COLUMN {colName} {colType};").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Aviso ao adicionar coluna '{colName}': {ex.Message}");
                    }
                }
            }

            try
            {
                await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_ClipboardItems_IsPinned_CreatedAt ON ClipboardItems(IsPinned, CreatedAt);").ConfigureAwait(false);
                await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_ClipboardItems_Type ON ClipboardItems(Type);").ConfigureAwait(false);
                await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_ClipboardItems_ContentHash ON ClipboardItems(ContentHash);").ConfigureAwait(false);
            }
            catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro na validação de schema: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _db?.CloseAsync().Wait(150);
            _db = null;
            _isInitialized = false;
        }
        catch { }
    }

    public async Task<List<ClipboardItem>> GetItemsAsync(string? filterCategory = "Todos", string? searchText = null)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return new List<ClipboardItem>();

            return await Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        string normalizedQuery = TextNormalizationHelper.RemoveDiacritics(searchText.Trim());
                        string pattern = $"%{normalizedQuery}%";
                        string rawPattern = $"%{searchText.Trim()}%";
                        string sql;

                        if (string.Equals(filterCategory, "Textos", StringComparison.OrdinalIgnoreCase))
                        {
                            sql = @"SELECT * FROM ClipboardItems 
                                    WHERE Type != 'Image' 
                                      AND (NormalizedSearchContent LIKE ? OR Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                                    ORDER BY IsPinned DESC, CreatedAt DESC";
                            return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern).ConfigureAwait(false);
                        }
                        else if (string.Equals(filterCategory, "Imagens", StringComparison.OrdinalIgnoreCase))
                        {
                            sql = @"SELECT * FROM ClipboardItems 
                                    WHERE Type = 'Image' 
                                      AND (NormalizedSearchContent LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                                    ORDER BY IsPinned DESC, CreatedAt DESC";
                            return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern).ConfigureAwait(false);
                        }
                        else if (string.Equals(filterCategory, "Fixados", StringComparison.OrdinalIgnoreCase))
                        {
                            sql = @"SELECT * FROM ClipboardItems 
                                    WHERE IsPinned = 1 
                                      AND (NormalizedSearchContent LIKE ? OR Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                                    ORDER BY CreatedAt DESC";
                            return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern).ConfigureAwait(false);
                        }
                        else
                        {
                            sql = @"SELECT * FROM ClipboardItems 
                                    WHERE (NormalizedSearchContent LIKE ? OR Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                                    ORDER BY IsPinned DESC, CreatedAt DESC";
                            return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern).ConfigureAwait(false);
                        }
                    }

                    string baseSql = filterCategory switch
                    {
                        var f when string.Equals(f, "Textos", StringComparison.OrdinalIgnoreCase) =>
                            "SELECT * FROM ClipboardItems WHERE Type != 'Image' ORDER BY IsPinned DESC, CreatedAt DESC",
                        var f when string.Equals(f, "Imagens", StringComparison.OrdinalIgnoreCase) =>
                            "SELECT * FROM ClipboardItems WHERE Type = 'Image' ORDER BY IsPinned DESC, CreatedAt DESC",
                        var f when string.Equals(f, "Fixados", StringComparison.OrdinalIgnoreCase) =>
                            "SELECT * FROM ClipboardItems WHERE IsPinned = 1 ORDER BY CreatedAt DESC",
                        _ =>
                            "SELECT * FROM ClipboardItems ORDER BY IsPinned DESC, CreatedAt DESC"
                    };

                    return await _db.QueryAsync<ClipboardItem>(baseSql).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro ao consultar itens no SQLite: {ex.Message}");
                    return new List<ClipboardItem>();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetItemsAsync falhou: {ex.Message}");
            return new List<ClipboardItem>();
        }
    }

    public async Task<ClipboardItem?> GetItemByIdAsync(int id)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return null;
            return await Task.Run(async () =>
                await _db.Table<ClipboardItem>().FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false)
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetItemByIdAsync erro: {ex.Message}");
            return null;
        }
    }

    public async Task<int> AddItemAsync(ClipboardItem item)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;
            return await Task.Run(async () =>
            {
                item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
                return await _db.InsertAsync(item).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddItemAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> UpdateItemAsync(ClipboardItem item)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;
            return await Task.Run(async () =>
            {
                item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
                return await _db.UpdateAsync(item).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateItemAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> DeleteItemAsync(ClipboardItem item)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;
            return await Task.Run(async () =>
            {
                if (item.IsImage)
                {
                    _storageService.DeleteFileIfExists(item.FilePath);
                }
                return await _db.DeleteAsync(item).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeleteItemAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> TogglePinAsync(int id)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;
            return await Task.Run(async () =>
            {
                return await _db.ExecuteAsync(
                    "UPDATE ClipboardItems SET IsPinned = CASE WHEN IsPinned = 1 THEN 0 ELSE 1 END WHERE Id = ?",
                    id).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TogglePinAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> ClearHistoryAsync(bool keepPinned = true)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;
            return await Task.Run(async () =>
            {
                List<ClipboardItem> itemsToDelete;
                if (keepPinned)
                {
                    itemsToDelete = await _db.Table<ClipboardItem>().Where(i => !i.IsPinned).ToListAsync().ConfigureAwait(false);
                }
                else
                {
                    itemsToDelete = await _db.Table<ClipboardItem>().ToListAsync().ConfigureAwait(false);
                }

                foreach (var item in itemsToDelete)
                {
                    if (item.IsImage)
                    {
                        _storageService.DeleteFileIfExists(item.FilePath);
                    }
                }

                if (keepPinned)
                {
                    await _db.ExecuteAsync("DELETE FROM ClipboardItems WHERE IsPinned = 0").ConfigureAwait(false);
                }
                else
                {
                    await _db.ExecuteAsync("DELETE FROM ClipboardItems").ConfigureAwait(false);
                }

                return itemsToDelete.Count;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ClearHistoryAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<string?> GetLastItemHashAsync()
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return null;
            return await Task.Run(async () =>
            {
                var lastItem = await _db.Table<ClipboardItem>().OrderByDescending(i => i.Id).FirstOrDefaultAsync().ConfigureAwait(false);
                return lastItem?.ContentHash;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetLastItemHashAsync erro: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IsDuplicateRecentHashAsync(string hash, int withinSeconds = 15)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return false;

            return await Task.Run(async () =>
            {
                var lastItem = await _db.Table<ClipboardItem>().OrderByDescending(i => i.Id).FirstOrDefaultAsync().ConfigureAwait(false);
                if (lastItem != null && string.Equals(lastItem.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var cutoff = DateTime.UtcNow.AddSeconds(-withinSeconds);
                var recentMatch = await _db.Table<ClipboardItem>()
                    .Where(i => i.ContentHash == hash && i.CreatedAt >= cutoff)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                return recentMatch != null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IsDuplicateRecentHashAsync erro: {ex.Message}");
            return false;
        }
    }

    public async Task<int> AutoCleanByAgeAsync(int maxDays)
    {
        if (maxDays <= 0) return 0;
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;

            return await Task.Run(async () =>
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-maxDays);
                var oldItems = await _db.Table<ClipboardItem>()
                    .Where(i => !i.IsPinned && i.CreatedAt < cutoffDate)
                    .ToListAsync()
                    .ConfigureAwait(false);

                int removed = 0;
                foreach (var item in oldItems)
                {
                    if (item.IsImage)
                    {
                        _storageService.DeleteFileIfExists(item.FilePath);
                    }
                    await _db.DeleteAsync(item).ConfigureAwait(false);
                    removed++;
                }

                return removed;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoCleanByAgeAsync erro: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> AutoCleanBySizeAsync(long maxBytes)
    {
        if (maxBytes <= 0) return 0;
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (_db == null) return 0;

            return await Task.Run(async () =>
            {
                long currentTotal = _storageService.GetTotalStorageSize();
                if (currentTotal <= maxBytes) return 0;

                var candidates = await _db.Table<ClipboardItem>()
                    .Where(i => !i.IsPinned)
                    .OrderBy(i => i.CreatedAt)
                    .ToListAsync()
                    .ConfigureAwait(false);

                int removed = 0;
                foreach (var item in candidates)
                {
                    if (currentTotal <= maxBytes) break;

                    long freed = item.ByteSize;
                    if (item.IsImage && !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                    {
                        try
                        {
                            freed = new FileInfo(item.FilePath).Length;
                        }
                        catch { }
                        _storageService.DeleteFileIfExists(item.FilePath);
                    }

                    await _db.DeleteAsync(item).ConfigureAwait(false);
                    currentTotal -= freed;
                    removed++;
                }

                return removed;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoCleanBySizeAsync erro: {ex.Message}");
            return 0;
        }
    }
}
