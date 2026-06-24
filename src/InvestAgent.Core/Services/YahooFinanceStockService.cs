using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

/// <summary>
/// Yahoo Finance 股票数据服务。
/// 通过 Yahoo Finance 的非官方 API（v8 chart / v10 quoteSummary）提供行情和K线数据。
/// 自动将 A 股代码转换为 Yahoo 格式（.SS 上交所 / .SZ 深交所）。
/// 注意：Yahoo 不提供 A 股主力资金流向和完整新闻接口。
/// </summary>
public class YahooFinanceStockService : IStockDataService
{
    private readonly HttpClient _http;
    private readonly IHttpCache _cache;
    private readonly ILogger<YahooFinanceStockService> _logger;

    /// <summary>请求限速信号量（最多 2 个并发请求）</summary>
    private static readonly SemaphoreSlim _throttle = new(2, 2);

    public string SourceName => "Yahoo Finance";

    public YahooFinanceStockService(HttpClient http, IHttpCache cache, ILogger<YahooFinanceStockService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    // ── 公开接口实现 ───────────────────────────────────────

    /// <inheritdoc />
    public async Task<StockQuote?> GetCurrentPriceAsync(string symbol)
    {
        var yahooSymbol = ToYahooSymbol(symbol);
        var data = await FetchChartAsync(yahooSymbol, "1d", "1d");
        if (data == null) return null;

        var meta = data.Value.Meta;
        return new StockQuote
        {
            Symbol = symbol,
            Name = meta.Name,
            Price = meta.Price,
            PreClose = meta.PreviousClose,
            ChangePercent = meta.PreviousClose != 0
                ? (meta.Price - meta.PreviousClose) / meta.PreviousClose * 100 : 0,
            ChangeAmount = meta.Price - meta.PreviousClose,
            High = meta.High,
            Low = meta.Low,
            Open = meta.Open,
            Volume = meta.Volume,
            UpdateTime = DateTime.Now
        };
    }

    /// <inheritdoc />
    public async Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30)
    {
        // 根据天数选择最优时间范围以减少数据传输
        var range = days <= 5 ? "5d"
            : days <= 30 ? "1mo"
            : days <= 90 ? "3mo"
            : days <= 180 ? "6mo"
            : days <= 365 ? "1y"
            : days <= 730 ? "2y"
            : days <= 1825 ? "5y"
            : "10y";
        var yahooSymbol = ToYahooSymbol(symbol);
        var data = await FetchChartAsync(yahooSymbol, range, "1d");
        return data == null ? new List<StockKLine>() : ToKLineList(symbol, data.Value).TakeLast(Math.Max(1, days)).ToList();
    }

    /// <inheritdoc />
    public async Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36)
    {
        var range = months <= 12 ? "1y"
            : months <= 24 ? "2y"
            : months <= 60 ? "5y"
            : months <= 120 ? "10y"
            : "max";
        var yahooSymbol = ToYahooSymbol(symbol);
        var data = await FetchChartAsync(yahooSymbol, range, "1mo");
        return data == null ? new List<StockKLine>() : ToKLineList(symbol, data.Value).TakeLast(Math.Max(1, months)).ToList();
    }

    /// <inheritdoc />
    public async Task<List<StockQuote>> SearchStockAsync(string keyword)
    {
        // Yahoo Finance 搜索需要 crumb/cookie，此处使用本地常见股票映射
        var commonStocks = new Dictionary<string, string>
        {
            ["茅台"] = "600519.SS", ["五粮液"] = "000858.SZ", ["宁德"] = "300750.SZ",
            ["比亚迪"] = "002594.SZ", ["招商银行"] = "600036.SS", ["平安"] = "601318.SS",
            ["apple"] = "AAPL", ["tesla"] = "TSLA", ["贵州"] = "600519.SS"
        };
        var results = new List<StockQuote>();
        foreach (var kv in commonStocks)
        {
            if (kv.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var parts = kv.Value.Split('.');
                results.Add(new StockQuote { Symbol = parts[0], Name = kv.Key, Price = 0 });
            }
        }
        return await Task.FromResult(results);
    }

    /// <inheritdoc />
    public async Task<KeyMetrics?> GetKeyMetricsAsync(string symbol)
    {
        var yahooSymbol = ToYahooSymbol(symbol);
        var data = await FetchQuoteSummaryAsync(yahooSymbol);
        if (data == null) return null;

        return new KeyMetrics
        {
            Symbol = symbol,
            Name = data.Value.Name,
            PE = data.Value.PE,
            PB = data.Value.PB,
            ROE = data.Value.ROE,
            ROA = data.Value.ROE * 0.3m, // Yahoo 不提供 ROA，基于 ROE 近似估算
            GrossMargin = data.Value.GrossMargin,
            NetMargin = data.Value.NetMargin,
            RevenueGrowth = data.Value.RevenueGrowth,
            ProfitGrowth = data.Value.ProfitGrowth,
            DebtRatio = data.Value.DebtRatio,
            MarketCap = data.Value.MarketCap,
            ReportDate = DateTime.Now.AddMonths(-3)
        };
    }

    /// <inheritdoc />
    public async Task<List<KeyMetrics>> GetKeyMetricsHistoryAsync(string symbol, int maxReports = 4)
    {
        // Yahoo v10 仅返回最新一期，历史序列需其他数据源补充
        var latest = await GetKeyMetricsAsync(symbol);
        if (latest is null) return new List<KeyMetrics>();
        return new List<KeyMetrics> { latest };
    }

    /// <inheritdoc />
    public async Task<string> GetMainBusinessAsync(string symbol)
    {
        var quote = await GetCurrentPriceAsync(symbol);
        var name = quote?.Name ?? symbol;
        return $"{name} 的主营业务可参考其公开披露信息；当前数据源未提供结构化主营业务字段。";
    }

    /// <inheritdoc />
    public async Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5)
    {
        // Yahoo Finance 当前实现未开启新闻接口
        return await Task.FromResult(new List<NewsItem>
        {
            new()
            {
                Title = "新闻数据缺失",
                Summary = "Yahoo Finance 在当前实现下未开启新闻接口，请切换 composite 数据源并配置 Alpha Vantage。",
                Source = "Yahoo Finance",
                PublishTime = DateTime.Now,
                Sentiment = "neutral",
                IsDataAvailable = false,
                DataNote = "建议使用 composite 数据源。"
            }
        });
    }

    /// <inheritdoc />
    public async Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20)
    {
        // Yahoo Finance 不提供 A 股主力资金流向
        return await Task.FromResult(new List<CapitalFlowItem>
        {
            new()
            {
                Symbol = symbol,
                Date = DateTime.Now,
                Source = "Yahoo Finance",
                IsApproximate = true,
                IsDataAvailable = false,
                DataNote = "Yahoo Finance 不提供 A 股主力资金流向，请使用 composite 数据源并配置 Finnhub。"
            }
        });
    }

    // ── 内部辅助方法 ───────────────────────────────────────

    /// <summary>
    /// 将 A 股代码转换为 Yahoo Finance 符号格式。
    /// 上交所（6 开头）→ .SS，深交所 → .SZ，美股保持原样。
    /// </summary>
    private static string ToYahooSymbol(string symbol)
    {
        if (symbol.All(char.IsLetter)) return symbol;                 // 美股
        if (symbol.StartsWith("6")) return $"{symbol}.SS";           // 上交所
        return $"{symbol}.SZ";                                        // 深交所
    }

    /// <summary>带缓存和限速的 HTTP GET 请求</summary>
    private async Task<string> CachedGetAsync(string url, TimeSpan ttl)
    {
        var cached = await _cache.GetAsync(url);
        if (cached != null) return cached;

        await _throttle.WaitAsync();
        try
        {
            var result = await _http.GetStringAsync(url);
            await _cache.SetAsync(url, result, ttl);
            return result;
        }
        finally { _throttle.Release(); }
    }

    /// <summary>从 Yahoo v8 chart API 获取 OHLCV 数据</summary>
    private async Task<ChartData?> FetchChartAsync(string symbol, string range, string interval)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}" +
                      $"?range={range}&interval={interval}";
            var json = await CachedGetAsync(url, TimeSpan.FromSeconds(60));
            using var doc = JsonDocument.Parse(json);
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");
            var timestamps = result.GetProperty("timestamp");
            var quotes = result.GetProperty("indicators").GetProperty("quote")[0];

            return new ChartData
            {
                Meta = new MetaData
                {
                    Name = meta.TryGetProperty("longName", out var n) ? n.GetString() ?? symbol : symbol,
                    Price = GetDecimal(meta, "regularMarketPrice"),
                    PreviousClose = GetDecimal(meta, "previousClose"),
                    High = GetDecimal(meta, "regularMarketDayHigh"),
                    Low = GetDecimal(meta, "regularMarketDayLow"),
                    Open = GetDecimal(meta, "regularMarketOpen"),
                    Volume = GetDecimal(meta, "regularMarketVolume"),
                },
                Timestamps = timestamps.EnumerateArray().Select(t => t.GetInt64()).ToArray(),
                Opens = quotes.GetProperty("open").EnumerateArray().Select(ToDecimal).ToArray(),
                Highs = quotes.GetProperty("high").EnumerateArray().Select(ToDecimal).ToArray(),
                Lows = quotes.GetProperty("low").EnumerateArray().Select(ToDecimal).ToArray(),
                Closes = quotes.GetProperty("close").EnumerateArray().Select(ToDecimal).ToArray(),
                Volumes = quotes.GetProperty("volume").EnumerateArray().Select(ToDecimal).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yahoo Finance 图表数据获取失败: {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>从 Yahoo v10 quoteSummary API 获取财务摘要数据</summary>
    private async Task<SummaryData?> FetchQuoteSummaryAsync(string symbol)
    {
        try
        {
            var url = $"https://query2.finance.yahoo.com/v10/finance/quoteSummary/{symbol}" +
                      "?modules=defaultKeyStatistics,financialData,price";
            var json = await CachedGetAsync(url, TimeSpan.FromMinutes(5));
            using var doc = JsonDocument.Parse(json);
            var summary = doc.RootElement.GetProperty("quoteSummary").GetProperty("result")[0];

            var keyStats = summary.TryGetProperty("defaultKeyStatistics", out var ks) ? ks : default;
            var finData = summary.TryGetProperty("financialData", out var fd) ? fd : default;
            var price = summary.TryGetProperty("price", out var pr) ? pr : default;

            return new SummaryData
            {
                Name = GetString(price, "longName") ?? GetString(price, "shortName") ?? symbol,
                PE = GetDecimal(ks, "forwardPE"),
                PB = GetDecimal(ks, "priceToBook"),
                ROE = GetDecimal(finData, "returnOnEquity") * 100,
                GrossMargin = GetDecimal(finData, "grossMargins") * 100,
                NetMargin = GetDecimal(finData, "profitMargins") * 100,
                RevenueGrowth = GetDecimal(finData, "revenueGrowth") * 100,
                ProfitGrowth = GetDecimal(finData, "earningsGrowth") * 100,
                DebtRatio = GetDecimal(finData, "debtToEquity"),
                MarketCap = GetDecimal(price, "marketCap")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yahoo Finance 摘要数据获取失败: {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>将 ChartData 结构转换为 K 线列表</summary>
    private static List<StockKLine> ToKLineList(string symbol, ChartData data)
    {
        var list = new List<StockKLine>();
        for (int i = 0; i < data.Timestamps.Length && i < data.Closes.Length; i++)
        {
            if (data.Closes[i] == 0 && data.Opens[i] == 0) continue; // 跳过无效数据点
            list.Add(new StockKLine
            {
                Symbol = symbol,
                Date = DateTimeOffset.FromUnixTimeSeconds(data.Timestamps[i]).DateTime,
                Open = data.Opens[i],
                High = data.Highs[i],
                Low = data.Lows[i],
                Close = data.Closes[i],
                Volume = data.Volumes[i]
            });
        }
        return list;
    }

    /// <summary>安全从 JSON 元素提取 decimal（支持 raw 嵌套格式）</summary>
    private static decimal GetDecimal(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object) return 0;
        if (e.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
            if (v.TryGetProperty("raw", out var raw)) return raw.GetDecimal();
        }
        return 0;
    }

    private static string? GetString(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        return e.TryGetProperty(prop, out var v) ? v.GetString() : null;
    }

    private static decimal ToDecimal(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDecimal() : 0;

    // ── 内部数据结构 ───────────────────────────────────────

    /// <summary>Yahoo v8 Chart API 返回的完整图表数据</summary>
    private struct ChartData
    {
        public MetaData Meta;
        public long[] Timestamps;
        public decimal[] Opens, Highs, Lows, Closes, Volumes;
    }

    /// <summary>图表元数据（当前快照）</summary>
    private struct MetaData
    {
        public string Name;
        public decimal Price, PreviousClose, High, Low, Open, Volume;
    }

    /// <summary>Yahoo v10 quoteSummary 返回的财务摘要数据</summary>
    private struct SummaryData
    {
        public string Name;
        public decimal PE, PB, ROE, GrossMargin, NetMargin, RevenueGrowth, ProfitGrowth, DebtRatio, MarketCap;
    }
}
