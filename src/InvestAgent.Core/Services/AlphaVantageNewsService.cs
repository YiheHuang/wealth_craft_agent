using System.Text.Json;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

public class AlphaVantageNewsService
{
    private readonly HttpClient _http;
    private readonly IHttpCache _cache;
    private readonly AgentOptions _options;
    private readonly ILogger<AlphaVantageNewsService> _logger;

    public AlphaVantageNewsService(HttpClient http, IHttpCache cache, AgentOptions options, ILogger<AlphaVantageNewsService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count)
    {
        if (!_options.AlphaVantageEnabled || string.IsNullOrWhiteSpace(_options.AlphaVantageApiKey))
        {
            return [new NewsItem
            {
                Title = "新闻数据不可用",
                Summary = "Alpha Vantage 未启用或未配置 API Key。",
                Source = "Alpha Vantage",
                PublishTime = DateTime.Now,
                Sentiment = "neutral",
                IsDataAvailable = false,
                DataNote = "请配置 Alpha Vantage API Key。"
            }];
        }

        var url = $"https://www.alphavantage.co/query?function=NEWS_SENTIMENT&tickers={Uri.EscapeDataString(symbol)}&limit={Math.Max(1, count)}&apikey={_options.AlphaVantageApiKey}";
        var json = await CachedGetAsync(url, TimeSpan.FromMinutes(5));
        var result = new List<NewsItem>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("feed", out var feed) || feed.ValueKind != JsonValueKind.Array)
            {
                return [new NewsItem
                {
                    Title = "新闻数据暂无",
                    Summary = "Alpha Vantage 暂未返回该标的新闻。",
                    Source = "Alpha Vantage",
                    PublishTime = DateTime.Now,
                    Sentiment = "neutral",
                    IsDataAvailable = false,
                    DataNote = "可稍后重试。"
                }];
            }

            foreach (var item in feed.EnumerateArray().Take(count))
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "新闻" : "新闻";
                var summary = item.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                var source = item.TryGetProperty("source", out var src) ? src.GetString() ?? "Alpha Vantage" : "Alpha Vantage";
                var articleUrl = item.TryGetProperty("url", out var uu) ? uu.GetString() ?? "" : "";
                var sentimentLabel = item.TryGetProperty("overall_sentiment_label", out var sl)
                    ? sl.GetString()?.ToLowerInvariant() ?? "neutral"
                    : "neutral";
                var sentiment = sentimentLabel.Contains("bearish") ? "negative"
                    : sentimentLabel.Contains("bullish") ? "positive"
                    : "neutral";

                var publishTime = DateTime.Now;
                if (item.TryGetProperty("time_published", out var tp) && tp.GetString() is string raw && raw.Length >= 8)
                {
                    DateTime.TryParse(raw[..8], out publishTime);
                }

                result.Add(new NewsItem
                {
                    Title = title,
                    Summary = summary,
                    Content = summary,
                    Url = articleUrl,
                    Source = source,
                    PublishTime = publishTime,
                    Sentiment = sentiment,
                    IsDataAvailable = true,
                    DataNote = ""
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AlphaVantage 新闻解析失败");
        }

        if (result.Count == 0)
        {
            result.Add(new NewsItem
            {
                Title = "新闻数据解析失败",
                Summary = "已请求新闻数据，但解析失败。",
                Content = "已请求新闻数据，但解析失败。",
                Url = "",
                Source = "Alpha Vantage",
                PublishTime = DateTime.Now,
                Sentiment = "neutral",
                IsDataAvailable = false,
                DataNote = "请检查 API 返回结构。"
            });
        }

        return result;
    }

    private async Task<string> CachedGetAsync(string url, TimeSpan ttl)
    {
        var cached = await _cache.GetAsync(url);
        if (cached != null) return cached;

        var text = await _http.GetStringAsync(url);
        await _cache.SetAsync(url, text, ttl);
        return text;
    }
}
