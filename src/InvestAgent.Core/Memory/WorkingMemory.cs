using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Memory;

/// <summary>
/// 工作内存的默认实现。
/// 基于 ConcurrentDictionary 的线程安全缓存，每 5 分钟自动清理过期条目。
/// 实现了 <see cref="IDisposable"/> 以确保定时器正确释放。
/// </summary>
public class WorkingMemory : IWorkingMemory, IDisposable
{
    /// <summary>内部缓存存储（线程安全字典）</summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private readonly ILogger<WorkingMemory> _logger;

    /// <summary>过期清理定时器（每 5 分钟触发一次）</summary>
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// 内部缓存条目，包含数据和过期时间。
    /// </summary>
    private class CacheEntry
    {
        /// <summary>缓存的实际数据对象</summary>
        public object Data { get; set; } = null!;

        /// <summary>过期时间（UTC）</summary>
        public DateTime Expiry { get; set; }
    }

    public WorkingMemory(ILogger<WorkingMemory> logger)
    {
        _logger = logger;
        // 启动定时清理任务，每 5 分钟扫描并移除过期条目
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.Now)
        {
            _logger.LogDebug("WorkingMemory 命中: {Key}", key);
            return Task.FromResult((T?)entry.Data);
        }

        return SetAndReturnAsync(key, factory, ttl);
    }

    /// <summary>内部方法：调用工厂生成数据并缓存后返回</summary>
    private async Task<T?> SetAndReturnAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        _logger.LogDebug("WorkingMemory 未命中: {Key}, 执行工厂方法", key);
        var data = await factory();
        Set(key, data, ttl);
        return data;
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        _cache[key] = new CacheEntry { Data = value!, Expiry = DateTime.Now.Add(ttl) };
    }

    /// <inheritdoc />
    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.Now)
            return (T)entry.Data;
        return default;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// 定时清理回调：扫描并移除所有已过期的缓存条目。
    /// </summary>
    /// <param name="state">定时器状态（未使用）</param>
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

    /// <summary>释放定时器资源</summary>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
