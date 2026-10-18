using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClipVault.Models;
using SQLite;

namespace ClipVault.Services;

public class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _db;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;

    public DatabaseService(ISettingsService settingsService, IStorageService storageService)
    {
        _settingsService = settingsService;
        _storageService = storageService;
    }

    public async Task InitializeAsync()
    {
        if (_db != null) return;

        AppEnvironment.EnsureDirectoriesAndMigration();
        var dbPath = AppEnvironment.DatabasePath;

        _db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
        await _db.CreateTableAsync<ClipboardItem>();
    }

    public void Dispose()
    {
        try
        {
            _db?.CloseAsync().Wait(150);
            _db = null;
        }
        catch { }
    }

    public async Task<List<ClipboardItem>> GetItemsAsync(string? filterCategory = "Todos", string? searchText = null)
    {
        await InitializeAsync();
        if (_db == null) return new List<ClipboardItem>();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string pattern = $"%{searchText.Trim()}%";
            string sql;

            if (string.Equals(filterCategory, "Textos", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE Type != 'Image' 
                          AND (Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY IsPinned DESC, CreatedAt DESC";
            }
            else if (string.Equals(filterCategory, "Imagens", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE Type = 'Image' 
                          AND (PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY IsPinned DESC, CreatedAt DESC";
                return await _db.QueryAsync<ClipboardItem>(sql, pattern, pattern, pattern);
            }
            else if (string.Equals(filterCategory, "Fixados", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE IsPinned = 1 
                          AND (Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY CreatedAt DESC";
            }
            else
            {
                sql = @"SELECT * FROM ClipboardItems 
                        WHERE (Content LIKE ? OR PreviewText LIKE ? OR ExtractedText LIKE ? OR ImageTags LIKE ?) 
                        ORDER BY IsPinned DESC, CreatedAt DESC";
            }

            return await _db.QueryAsync<ClipboardItem>(sql, pattern, pattern, pattern, pattern);
        }

        // Sem termo de busca: consulta padrão
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
        return await _db!.InsertAsync(item);
    }

    public async Task<int> UpdateItemAsync(ClipboardItem item)
    {
        await InitializeAsync();
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

        long currentSize = _storageService.GetTotalStorageSize();
        if (currentSize <= maxBytes) return 0;

        // Buscar itens não fixados, do mais antigo para o mais recente
        var unpinnedItems = await _db!.Table<ClipboardItem>()
            .Where(i => !i.IsPinned)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();

        int removed = 0;
        foreach (var item in unpinnedItems)
        {
            if (item.IsImage)
            {
                _storageService.DeleteFileIfExists(item.FilePath);
            }
            await _db.DeleteAsync(item);
            removed++;

            currentSize = _storageService.GetTotalStorageSize();
            if (currentSize <= maxBytes) break;
        }

        return removed;
    }
}
