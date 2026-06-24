using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvestAgent.Core.Configuration;

namespace InvestAgent.Core.Services;

/// <summary>
/// 基于文件系统的 HTTP 缓存实现。
/// 将 HTTP 响应序列化为 JSON 文件存储于临时目录中。
/// URL 通过 SHA256 哈希生成文件名，并在文件内容中记录过期时间。
/// 缓存写入失败不影响主流程（静默忽略异常）。
/// </summary>
public class FileHttpCache : IHttpCache
{
    /// <summary>缓存文件存储目录</summary>
    private readonly string _cacheDir;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public FileHttpCache(AgentOptions options)
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "InvestAgent", "api-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string url)
    {
        var path = GetCachePath(url);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);

        try
        {
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json);
            // 检查过期
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

    /// <inheritdoc />
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

    /// <summary>
    /// 根据 URL 生成缓存文件路径。
    /// 使用 SHA256 哈希的前 16 位十六进制作为文件名。
    /// </summary>
    private string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        return Path.Combine(_cacheDir, $"{hash}.json");
    }

    /// <summary>
    /// 内部缓存条目结构——包含数据内容和过期时间。
    /// </summary>
    private class CacheEntry
    {
        /// <summary>HTTP 响应内容</summary>
        public string Data { get; set; } = "";

        /// <summary>过期时间</summary>
        public DateTime Expiry { get; set; }
    }
}
