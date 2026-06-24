namespace InvestAgent.Core.Services;

/// <summary>
/// HTTP 响应缓存接口。
/// 提供基于 URL 的 HTTP GET 响应缓存能力，
/// 用于减少对外部 API 的重复请求，降低速率限制风险和网络开销。
/// </summary>
public interface IHttpCache
{
    /// <summary>
    /// 根据 URL 获取缓存的 HTTP 响应内容。
    /// </summary>
    /// <param name="url">请求 URL</param>
    /// <returns>缓存的响应内容；若缓存未命中或已过期则返回 null</returns>
    Task<string?> GetAsync(string url);

    /// <summary>
    /// 缓存 HTTP 响应内容。
    /// </summary>
    /// <param name="url">请求 URL（作为缓存键）</param>
    /// <param name="response">响应内容</param>
    /// <param name="ttl">缓存生存时间</param>
    Task SetAsync(string url, string response, TimeSpan ttl);
}
