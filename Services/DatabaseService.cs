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

                var connection = new SQLiteAsyncConnection(
                    dbPath,
                    SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);

                await connection.CreateTableAsync<ClipboardItem>().ConfigureAwait(false);
                _db = connection;
                _isInitialized = true;
            }).ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
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
        await InitializeAsync().ConfigureAwait(false);
        if (_db == null) return new List<ClipboardItem>();

        return await Task.Run(async () =>
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
        }).ConfigureAwait(false);
    }

    public async Task<ClipboardItem?> GetItemByIdAsync(int id)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
            await _db!.Table<ClipboardItem>().FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false)
        ).ConfigureAwait(false);
    }

    public async Task<int> AddItemAsync(ClipboardItem item)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
            return await _db!.InsertAsync(item).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<int> UpdateItemAsync(ClipboardItem item)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
            return await _db!.UpdateAsync(item).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<int> DeleteItemAsync(ClipboardItem item)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            if (item.IsImage)
            {
                _storageService.DeleteFileIfExists(item.FilePath);
            }
            return await _db!.DeleteAsync(item).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<int> TogglePinAsync(int id)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            return await _db!.ExecuteAsync(
                "UPDATE ClipboardItems SET IsPinned = CASE WHEN IsPinned = 1 THEN 0 ELSE 1 END WHERE Id = ?",
                id).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<int> ClearHistoryAsync(bool keepPinned = true)
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            List<ClipboardItem> itemsToDelete;
            if (keepPinned)
            {
                itemsToDelete = await _db!.Table<ClipboardItem>().Where(i => !i.IsPinned).ToListAsync().ConfigureAwait(false);
            }
            else
            {
                itemsToDelete = await _db!.Table<ClipboardItem>().ToListAsync().ConfigureAwait(false);
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

    public async Task<string?> GetLastItemHashAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            var lastItem = await _db!.Table<ClipboardItem>().OrderByDescending(i => i.Id).FirstOrDefaultAsync().ConfigureAwait(false);
            return lastItem?.ContentHash;
        }).ConfigureAwait(false);
    }

    public async Task<bool> IsDuplicateRecentHashAsync(string hash, int withinSeconds = 15)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
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

    public async Task<int> AutoCleanByAgeAsync(int maxDays)
    {
        if (maxDays <= 0) return 0;
        await InitializeAsync().ConfigureAwait(false);

        return await Task.Run(async () =>
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-maxDays);
            var oldItems = await _db!.Table<ClipboardItem>()
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

    public async Task<int> AutoCleanBySizeAsync(long maxBytes)
    {
        if (maxBytes <= 0) return 0;
        await InitializeAsync().ConfigureAwait(false);

        return await Task.Run(async () =>
        {
            long currentTotal = _storageService.GetTotalStorageSize();
            if (currentTotal <= maxBytes) return 0;

            var candidates = await _db!.Table<ClipboardItem>()
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
}
