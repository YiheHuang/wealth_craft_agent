namespace InvestAgent.Core.Services;

public interface IHttpCache
{
    Task<string?> GetAsync(string url);
    Task SetAsync(string url, string response, TimeSpan ttl);
}
