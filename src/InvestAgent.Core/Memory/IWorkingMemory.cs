namespace InvestAgent.Core.Memory;

public interface IWorkingMemory
{
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);
    void Set<T>(string key, T value, TimeSpan ttl);
    T? Get<T>(string key);
    void Remove(string key);
    void Clear();
}
