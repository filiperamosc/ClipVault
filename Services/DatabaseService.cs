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
    private readonly IVisionClassifierService _visionClassifierService;
    private bool _visionMigrated;

    public DatabaseService(
        ISettingsService settingsService,
        IStorageService storageService,
        IVisionClassifierService visionClassifierService)
    {
        _settingsService = settingsService;
        _storageService = storageService;
        _visionClassifierService = visionClassifierService;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized && _db != null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized && _db != null) return;

            AppEnvironment.EnsureDirectoriesAndMigration();
            var dbPath = AppEnvironment.DatabasePath;

            var connection = new SQLiteAsyncConnection(
                dbPath,
                SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);

            await connection.CreateTableAsync<ClipboardItem>();
            _db = connection;
            _isInitialized = true;

            if (!_visionMigrated)
            {
                _visionMigrated = true;
                _ = Task.Run(MigrateLegacyMetadataAsync);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task MigrateLegacyMetadataAsync()
    {
        try
        {
            if (_db == null) return;

            var unnormalized = await _db.Table<ClipboardItem>()
                                        .Where(i => i.NormalizedSearchContent == null || i.NormalizedSearchContent == "")
                                        .Take(200)
                                        .ToListAsync();
            foreach (var row in unnormalized)
            {
                row.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(row.Content, row.PreviewText, row.ExtractedText, row.ImageTags);
                await _db.UpdateAsync(row);
            }

            var imageRows = await _db.Table<ClipboardItem>()
                                     .Where(i => i.Type == "Image" && (i.ImageTags == null || i.ImageTags == "" || i.ImageTags.Contains("Interface") || i.ImageTags.Contains("elemento visual")))
                                     .Take(50)
                                     .ToListAsync();

            foreach (var img in imageRows)
            {
                if (!string.IsNullOrWhiteSpace(img.FilePath) && File.Exists(img.FilePath))
                {
                    var freshTags = await _visionClassifierService.ClassifyImageAsync(img.FilePath);
                    if (img.ImageTags != freshTags)
                    {
                        img.ImageTags = freshTags;
                        img.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(img.Content, img.PreviewText, img.ExtractedText, img.ImageTags);
                        await _db.UpdateAsync(img);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro na migração em segundo plano: {ex.Message}");
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
        await InitializeAsync();
        if (_db == null) return new List<ClipboardItem>();

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
                return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern);
            }
            else if (string.Equals(filterCategory, "Imagens", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE Type = 'Image' 
                          AND (NormalizedSearchContent LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY IsPinned DESC, CreatedAt DESC";
                return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern);
            }
            else if (string.Equals(filterCategory, "Fixados", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE IsPinned = 1 
                          AND (NormalizedSearchContent LIKE ? OR Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY CreatedAt DESC";
                return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern);
            }
            else
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE (NormalizedSearchContent LIKE ? OR Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY IsPinned DESC, CreatedAt DESC";
                return await _db.QueryAsync<ClipboardItem>(sql, pattern, rawPattern, rawPattern, rawPattern, rawPattern);
            }
        }

        var query = _db.Table<ClipboardItem>();

        if (string.Equals(filterCategory, "Textos", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.Type != "Image");
        }
        else if (string.Equals(filterCategory, "Imagens", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.Type == "Image");
        }
        else if (string.Equals(filterCategory, "Fixados", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.IsPinned == true);
        }

        var items = await query.ToListAsync();
        return items.OrderByDescending(i => i.IsPinned).ThenByDescending(i => i.CreatedAt).ToList();
    }

    public async Task<ClipboardItem?> GetItemByIdAsync(int id)
    {
        await InitializeAsync();
        return await _db!.Table<ClipboardItem>().FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<int> AddItemAsync(ClipboardItem item)
    {
        await InitializeAsync();
        item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
        return await _db!.InsertAsync(item);
    }

    public async Task<int> UpdateItemAsync(ClipboardItem item)
    {
        await InitializeAsync();
        item.NormalizedSearchContent = TextNormalizationHelper.BuildSearchVector(item.Content, item.PreviewText, item.ExtractedText, item.ImageTags);
        return await _db!.UpdateAsync(item);
    }

    public async Task<int> DeleteItemAsync(ClipboardItem item)
    {
        await InitializeAsync();
        if (item.IsImage)
        {
            _storageService.DeleteFileIfExists(item.FilePath);
        }
        return await _db!.DeleteAsync(item);
    }

    public async Task<int> TogglePinAsync(int id)
    {
        await InitializeAsync();
        var item = await _db!.Table<ClipboardItem>().FirstOrDefaultAsync(i => i.Id == id);
        if (item != null)
        {
            item.IsPinned = !item.IsPinned;
            return await _db.UpdateAsync(item);
        }
        return 0;
    }

    public async Task<int> ClearHistoryAsync(bool keepPinned = true)
    {
        await InitializeAsync();
        List<ClipboardItem> itemsToDelete;
        if (keepPinned)
        {
            itemsToDelete = await _db!.Table<ClipboardItem>().Where(i => !i.IsPinned).ToListAsync();
        }
        else
        {
            itemsToDelete = await _db!.Table<ClipboardItem>().ToListAsync();
        }

        foreach (var item in itemsToDelete)
        {
            if (item.IsImage)
            {
                _storageService.DeleteFileIfExists(item.FilePath);
            }
            await _db!.DeleteAsync(item);
        }

        return itemsToDelete.Count;
    }

    public async Task<string?> GetLastItemHashAsync()
    {
        await InitializeAsync();
        var lastItem = await _db!.Table<ClipboardItem>().OrderByDescending(i => i.Id).FirstOrDefaultAsync();
        return lastItem?.ContentHash;
    }

    public async Task<bool> IsDuplicateRecentHashAsync(string hash, int withinSeconds = 15)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        await InitializeAsync();
        if (_db == null) return false;

        var lastItem = await _db.Table<ClipboardItem>().OrderByDescending(i => i.Id).FirstOrDefaultAsync();
        if (lastItem != null && string.Equals(lastItem.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cutoff = DateTime.UtcNow.AddSeconds(-withinSeconds);
        var recentMatch = await _db.Table<ClipboardItem>()
            .Where(i => i.ContentHash == hash && i.CreatedAt >= cutoff)
            .FirstOrDefaultAsync();

        return recentMatch != null;
    }

    public async Task<int> AutoCleanByAgeAsync(int maxDays)
    {
        if (maxDays <= 0) return 0;
        await InitializeAsync();

        var cutoffDate = DateTime.UtcNow.AddDays(-maxDays);
        var oldItems = await _db!.Table<ClipboardItem>()
            .Where(i => !i.IsPinned && i.CreatedAt < cutoffDate)
            .ToListAsync();

        int removed = 0;
        foreach (var item in oldItems)
        {
            if (item.IsImage)
            {
                _storageService.DeleteFileIfExists(item.FilePath);
            }
            await _db.DeleteAsync(item);
            removed++;
        }

        return removed;
    }

    public async Task<int> AutoCleanBySizeAsync(long maxBytes)
    {
        if (maxBytes <= 0) return 0;
        await InitializeAsync();

        long currentTotal = _storageService.GetTotalStorageSize();
        if (currentTotal <= maxBytes) return 0;

        var candidates = await _db!.Table<ClipboardItem>()
            .Where(i => !i.IsPinned)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();

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

            await _db.DeleteAsync(item);
            currentTotal -= freed;
            removed++;
        }

        return removed;
    }
}
