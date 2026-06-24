using System.Text.Json;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

/// <summary>
/// Finnhub 资金流数据服务。
/// 注意：Finnhub 对个股无直接"资金流"接口，
/// 此实现使用 insider-sentiment（内部人情绪）指标进行近似估算，
/// 所有返回数据均标记 IsApproximate=true。
/// 资金流功能可能已按产品要求移除。
/// </summary>
public class FinnhubCapitalFlowService
{
    private readonly HttpClient _http;
    private readonly IHttpCache _cache;
    private readonly AgentOptions _options;
    private readonly ILogger<FinnhubCapitalFlowService> _logger;

    public FinnhubCapitalFlowService(HttpClient http, IHttpCache cache, AgentOptions options, ILogger<FinnhubCapitalFlowService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 获取资金流数据（近似估算）。
    /// 如果 Finnhub 未启用或 API Key 未配置，返回不可用标记。
    /// </summary>
    public async Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days)
    {
        if (!_options.FinnhubEnabled || string.IsNullOrWhiteSpace(_options.FinnhubApiKey))
        {
            return [CreateUnavailable(symbol, "Finnhub 未启用或未配置 API Key。")];
        }

        // Finnhub 对个股资金流无直接等价接口，利用 insider-sentiment 近似
        var from = DateTime.UtcNow.AddDays(-Math.Max(days, 1)).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var url = $"https://finnhub.io/api/v1/stock/insider-sentiment?symbol={Uri.EscapeDataString(symbol)}&from={from}&to={to}&token={_options.FinnhubApiKey}";
        var json = await CachedGetAsync(url, TimeSpan.FromMinutes(10));

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                return [CreateUnavailable(symbol, "Finnhub 无该标的可用资金流近似数据。")];
            }

            var rows = new List<CapitalFlowItem>();
            foreach (var item in data.EnumerateArray().Take(days))
            {
                // change 字段映射为主力净流入（乘以 1,000,000 作为近似量级）
                var change = GetDecimal(item, "change");
                // mspr（月买入卖出比率）映射为中单/小单
                var mspr = GetDecimal(item, "mspr");
                var date = DateTime.Now;
                if (item.TryGetProperty("year", out var y) && item.TryGetProperty("month", out var m))
                {
                    date = new DateTime(y.GetInt32(), m.GetInt32(), 1);
                }

                rows.Add(new CapitalFlowItem
                {
                    Symbol = symbol,
                    Date = date,
                    Source = "Finnhub(insider_sentiment)",
                    MainForce = change * 1000000m,
                    SuperLargeOrder = 0,
                    LargeOrder = 0,
                    MediumOrder = mspr * 100000m,
                    SmallOrder = -mspr * 100000m,
                    IsApproximate = true,
                    IsDataAvailable = true,
                    DataNote = "采用内部人情绪指标近似资金流，不等价于交易所主力净流入。"
                });
            }

            return rows.Count > 0 ? rows : [CreateUnavailable(symbol, "Finnhub 返回为空。")];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Finnhub 资金流解析失败");
            return [CreateUnavailable(symbol, $"Finnhub 解析失败: {ex.Message}")];
        }
    }

    /// <summary>创建数据不可用的占位条目</summary>
    private static CapitalFlowItem CreateUnavailable(string symbol, string note) => new()
    {
        Symbol = symbol,
        Date = DateTime.Now,
        Source = "Finnhub",
        IsApproximate = true,
        IsDataAvailable = false,
        DataNote = note
    };

    /// <summary>安全地从 JSON 元素中提取 decimal 值</summary>
    private static decimal GetDecimal(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        return 0;
    }

    /// <summary>带缓存的 HTTP GET 请求</summary>
    private async Task<string> CachedGetAsync(string url, TimeSpan ttl)
    {
        var cached = await _cache.GetAsync(url);
        if (cached != null) return cached;

        var text = await _http.GetStringAsync(url);
        await _cache.SetAsync(url, text, ttl);
        return text;
    }
}
