using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvestAgent.Core.Configuration;

namespace InvestAgent.Core.Services;

public class FileHttpCache : IHttpCache
{
    private readonly string _cacheDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public FileHttpCache(AgentOptions options)
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "InvestAgent", "api-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public Task<string?> GetAsync(string url)
    {
        var path = GetCachePath(url);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);

        try
        {
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json);
            if (entry == null || entry.Expiry < DateTime.Now)
            {
                File.Delete(path);
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>(entry.Data);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string url, string response, TimeSpan ttl)
    {
        try
        {
            var entry = new CacheEntry { Data = response, Expiry = DateTime.Now.Add(ttl) };
            var json = JsonSerializer.Serialize(entry, JsonOpts);
            File.WriteAllText(GetCachePath(url), json);
        }
        catch { /* 缓存写入失败不影响主流程 */ }
        return Task.CompletedTask;
    }

    private string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        return Path.Combine(_cacheDir, $"{hash}.json");
    }

    private class CacheEntry
    {
        public string Data { get; set; } = "";
        public DateTime Expiry { get; set; }
    }
}
