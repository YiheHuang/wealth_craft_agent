using System.Text.Json;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

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

    public async Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days)
    {
        if (!_options.FinnhubEnabled || string.IsNullOrWhiteSpace(_options.FinnhubApiKey))
        {
            return [CreateUnavailable(symbol, "Finnhub 未启用或未配置 API Key。")];
        }

        // Finnhub 对个股“资金流”无直接等价接口，这里用 sentiment 近似，显式标记为 approximate。
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
                var change = GetDecimal(item, "change");
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

    private static CapitalFlowItem CreateUnavailable(string symbol, string note) => new()
    {
        Symbol = symbol,
        Date = DateTime.Now,
        Source = "Finnhub",
        IsApproximate = true,
        IsDataAvailable = false,
        DataNote = note
    };

    private static decimal GetDecimal(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        return 0;
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

