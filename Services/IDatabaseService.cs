using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClipVault.Models;

namespace ClipVault.Services;

public interface IDatabaseService : IDisposable
{
    Task InitializeAsync();
    Task<List<ClipboardItem>> GetItemsAsync(string? filterCategory = "Todos", string? searchText = null);
    Task<ClipboardItem?> GetItemByIdAsync(int id);
    Task<int> AddItemAsync(ClipboardItem item);
    Task<int> UpdateItemAsync(ClipboardItem item);
    Task<int> DeleteItemAsync(ClipboardItem item);
    Task<int> TogglePinAsync(int id);
    Task<int> ClearHistoryAsync(bool keepPinned = true);
    Task<string?> GetLastItemHashAsync();
    Task<int> AutoCleanByAgeAsync(int maxDays);
    Task<int> AutoCleanBySizeAsync(long maxBytes);
}
