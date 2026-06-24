namespace InvestAgent.Core.Memory;

/// <summary>
/// 工作内存（短期缓存）接口。
/// 提供带 TTL（生存时间）的键值缓存功能，用于减少重复的 API 调用和数据计算。
/// 实现类应支持并发安全访问。
/// </summary>
public interface IWorkingMemory
{
    /// <summary>
    /// 获取或创建缓存项。若缓存命中且在有效期内则直接返回，
    /// 否则调用工厂方法生成数据并缓存。
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="factory">数据生成工厂方法</param>
    /// <param name="ttl">缓存生存时间</param>
    /// <returns>缓存或新生成的数据</returns>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);

    /// <summary>直接设置缓存项</summary>
    void Set<T>(string key, T value, TimeSpan ttl);

    /// <summary>获取缓存项（不会触发工厂方法）</summary>
    T? Get<T>(string key);

    /// <summary>移除指定缓存项</summary>
    void Remove(string key);

    /// <summary>清空所有缓存</summary>
    void Clear();
}
