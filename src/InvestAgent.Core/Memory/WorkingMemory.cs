using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Memory;

public class WorkingMemory : IWorkingMemory, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ILogger<WorkingMemory> _logger;
    private readonly Timer _cleanupTimer;

    private class CacheEntry
    {
        public object Data { get; set; } = null!;
        public DateTime Expiry { get; set; }
    }

    public WorkingMemory(ILogger<WorkingMemory> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.Now)
        {
            _logger.LogDebug("WorkingMemory 命中: {Key}", key);
            return Task.FromResult((T?)entry.Data);
        }

        return SetAndReturnAsync(key, factory, ttl);
    }

    private async Task<T?> SetAndReturnAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        _logger.LogDebug("WorkingMemory 未命中: {Key}, 执行工厂方法", key);
        var data = await factory();
        Set(key, data, ttl);
        return data;
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        _cache[key] = new CacheEntry { Data = value!, Expiry = DateTime.Now.Add(ttl) };
    }

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.Now)
            return (T)entry.Data;
        return default;
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void Clear()
    {
        _cache.Clear();
    }

    private void CleanupExpired(object? state)
    {
        var expiredKeys = _cache
            .Where(kv => kv.Value.Expiry <= DateTime.Now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
            _cache.TryRemove(key, out _);

        if (expiredKeys.Count > 0)
            _logger.LogDebug("清理过期缓存: {Count} 条", expiredKeys.Count);
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
