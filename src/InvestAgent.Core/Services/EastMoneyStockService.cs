using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

public class EastMoneyStockService : IStockDataService
{
    private readonly HttpClient _http;
    private readonly IHttpCache _cache;
    private readonly ILogger<EastMoneyStockService> _logger;
    private static readonly SemaphoreSlim _throttle = new(3, 3);
    public string SourceName => "东方财富";

    public EastMoneyStockService(HttpClient http, IHttpCache cache, ILogger<EastMoneyStockService> logger)
    {
        _cache = cache;
        _http = http;
        _logger = logger;
    }

    public async Task<StockQuote?> GetCurrentPriceAsync(string symbol)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/get?" +
                  $"secid={secid}&" +
                  "fields=f43,f44,f45,f46,f47,f48,f57,f58,f60,f116,f162,f167,f169,f170";
        _logger.LogInformation("东方财富: 获取 {Symbol} 实时行情", symbol);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null) return null;

        return new StockQuote
        {
            Symbol = symbol,
            Name = data.GetProperty("f58").GetString() ?? symbol,
            Price = GetDecimalProp(data, "f43") / 100m,
            Open = GetDecimalProp(data, "f46") / 100m,
            High = GetDecimalProp(data, "f44") / 100m,
            Low = GetDecimalProp(data, "f45") / 100m,
            PreClose = GetDecimalProp(data, "f60") / 100m,
            ChangePercent = GetDecimalProp(data, "f170") / 100m,
            ChangeAmount = GetDecimalProp(data, "f169") / 100m,
            Volume = GetDecimalProp(data, "f47"),
            Turnover = GetDecimalProp(data, "f48"),
            UpdateTime = DateTime.Now
        };
    }

    public async Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?" +
                  $"secid={secid}&" +
                  "fields1=f1,f2,f3&" +
                  "fields2=f51,f52,f53,f54,f55,f56,f57&" +
                  $"klt=101&fqt=1&end=20500101&lmt={days}";
        _logger.LogInformation("东方财富: 获取 {Symbol} {Days}日K线", symbol, days);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null) return new List<StockKLine>();

        var klines = data.GetProperty("klines");
        if (klines.ValueKind != JsonValueKind.Array) return new List<StockKLine>();

        var result = new List<StockKLine>();
        foreach (var line in klines.EnumerateArray())
        {
            var str = line.GetString();
            if (string.IsNullOrEmpty(str)) continue;
            var parts = str.Split(',');
            if (parts.Length < 6) continue;

            result.Add(new StockKLine
            {
                Symbol = symbol,
                Date = DateTime.Parse(parts[0]),
                Open = decimal.Parse(parts[1]),
                Close = decimal.Parse(parts[2]),
                High = decimal.Parse(parts[3]),
                Low = decimal.Parse(parts[4]),
                Volume = decimal.Parse(parts[5])
            });
        }
        return result;
    }

    public async Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?" +
                  $"secid={secid}&" +
                  "fields1=f1,f2,f3&" +
                  "fields2=f51,f52,f53,f54,f55,f56,f57&" +
                  $"klt=103&fqt=1&end=20500101&lmt={months}";
        _logger.LogInformation("东方财富: 获取 {Symbol} 月K线 {Months}个月", symbol, months);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null) return new List<StockKLine>();

        var klines = data.GetProperty("klines");
        if (klines.ValueKind != JsonValueKind.Array) return new List<StockKLine>();

        var result = new List<StockKLine>();
        foreach (var line in klines.EnumerateArray())
        {
            var str = line.GetString();
            if (string.IsNullOrEmpty(str)) continue;
            var parts = str.Split(',');
            if (parts.Length < 6) continue;

            result.Add(new StockKLine
            {
                Symbol = symbol,
                Date = DateTime.Parse(parts[0]),
                Open = decimal.Parse(parts[1]),
                Close = decimal.Parse(parts[2]),
                High = decimal.Parse(parts[3]),
                Low = decimal.Parse(parts[4]),
                Volume = decimal.Parse(parts[5])
            });
        }
        return result;
    }

    public async Task<List<StockQuote>> SearchStockAsync(string keyword)
    {
        var url = $"https://searchadapter.eastmoney.com/api/suggest/get?" +
                  $"input={Uri.EscapeDataString(keyword)}&type=14&" +
                  "token=D43BF722C8E33BDC906FB84D85E326E8&count=10";
        _logger.LogInformation("东方财富: 搜索股票 {Keyword}", keyword);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("QuotationCodeTable");
        if (data.ValueKind == JsonValueKind.Null) return new List<StockQuote>();

        var items = data.GetProperty("Data");
        if (items.ValueKind != JsonValueKind.Array) return new List<StockQuote>();

        var results = new List<StockQuote>();
        foreach (var item in items.EnumerateArray())
        {
            var code = item.GetProperty("Code").GetString() ?? "";
            var name = item.GetProperty("Name").GetString() ?? "";
            var market = item.GetProperty("MktNum").GetString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;

            results.Add(new StockQuote
            {
                Symbol = code,
                Name = $"{name} ({market})",
                Price = 0
            });
        }
        return results;
    }

    public async Task<KeyMetrics?> GetKeyMetricsAsync(string symbol)
    {
        var secid = GetSecId(symbol);
        string stockName = symbol;
        decimal pe = 0, pb = 0, marketCap = 0;
        try
        {
            var quote = await GetQuoteSummaryAsync(symbol, secid);
            stockName = quote.Name;
            pe = quote.PE;
            pb = quote.PB;
            marketCap = quote.MarketCap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "东方财富: 行情摘要获取失败，继续尝试财务主表 {Symbol}", symbol);
        }

        var finData = await GetFinancialDataAsync(symbol);

        return new KeyMetrics
        {
            Symbol = symbol,
            Name = string.IsNullOrWhiteSpace(stockName) ? symbol : stockName,
            PE = pe,
            PB = pb,
            ROE = finData.ROE,
            ROA = finData.ROA,
            GrossMargin = finData.GrossMargin,
            NetMargin = finData.NetMargin,
            RevenueGrowth = finData.RevenueGrowth,
            ProfitGrowth = finData.ProfitGrowth,
            DebtRatio = finData.DebtRatio,
            MarketCap = marketCap,
            ReportDate = finData.ReportDate
        };
    }

    public async Task<List<KeyMetrics>> GetKeyMetricsHistoryAsync(string symbol, int maxReports = 4)
    {
        var secid = GetSecId(symbol);
        string stockName = symbol;
        decimal pe = 0, pb = 0, marketCap = 0;
        try
        {
            var quote = await GetQuoteSummaryAsync(symbol, secid);
            stockName = quote.Name;
            pe = quote.PE;
            pb = quote.PB;
            marketCap = quote.MarketCap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "东方财富: 历史财务的行情摘要获取失败，继续返回财务主表 {Symbol}", symbol);
        }

        List<(decimal ROE, decimal ROA, decimal GrossMargin, decimal NetMargin, decimal RevenueGrowth, decimal ProfitGrowth, decimal DebtRatio, DateTime ReportDate)> list;
        try
        {
            list = await GetFinancialDataHistoryAsync(symbol, maxReports);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "东方财富: 历史财务主表获取失败，降级为单期财务 {Symbol}", symbol);
            var single = await GetFinancialDataAsync(symbol);
            list = new List<(decimal, decimal, decimal, decimal, decimal, decimal, decimal, DateTime)>
            {
                single
            };
        }

        return list.Select(fin => new KeyMetrics
        {
            Symbol = symbol,
            Name = stockName,
            PE = pe,
            PB = pb,
            ROE = fin.ROE,
            ROA = fin.ROA,
            GrossMargin = fin.GrossMargin,
            NetMargin = fin.NetMargin,
            RevenueGrowth = fin.RevenueGrowth,
            ProfitGrowth = fin.ProfitGrowth,
            DebtRatio = fin.DebtRatio,
            MarketCap = marketCap,
            ReportDate = fin.ReportDate
        }).ToList();
    }

    public async Task<string> GetMainBusinessAsync(string symbol)
    {
        try
        {
            var secid = GetSecId(symbol);
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&ut=bd1d9ddb04089700cf9c27f6f7426281&fields=f58,f127,f128";
            var json = await ThrottledGetAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return "";
            var name = data.TryGetProperty("f58", out var f58) ? f58.GetString() ?? symbol : symbol;
            var industry = data.TryGetProperty("f127", out var f127) ? f127.GetString() ?? "" : "";
            var board = data.TryGetProperty("f128", out var f128) ? f128.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(industry) && string.IsNullOrWhiteSpace(board))
                return $"{name} 的主营业务暂无结构化字段。";
            return $"{name} 主要业务方向可归纳为：行业“{industry}”，所属板块“{board}”。";
        }
        catch
        {
            return "";
        }
    }

    public async Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5)
    {
        var url = $"https://np-anotice-stock.eastmoney.com/api/security/ann?" +
                  $"page_size={count}&page_index=1&ann_type=A&" +
                  $"stock_list={symbol}&sr=-1";
        _logger.LogInformation("东方财富: 获取 {Symbol} 公告新闻", symbol);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null) return new List<NewsItem>();

        var list = data.GetProperty("list");
        if (list.ValueKind != JsonValueKind.Array) return new List<NewsItem>();

        var news = new List<NewsItem>();
        foreach (var item in list.EnumerateArray())
        {
            var title = item.TryGetProperty("title_ch", out var tc)
                ? (tc.GetString() ?? "公告") : "公告";
            var summary = "";
            if (item.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
            {
                var firstCol = cols[0];
                summary = firstCol.TryGetProperty("column_name", out var cn)
                    ? (cn.GetString() ?? "") : "";
            }

            DateTime pubTime = DateTime.Now;
            if (item.TryGetProperty("notice_date", out var nd) && nd.GetString() is string ds)
                DateTime.TryParse(ds, out pubTime);

            var content = summary;
            var noticeUrl = "";
            if (item.TryGetProperty("art_code", out var ac) && ac.GetString() is string artCode && !string.IsNullOrWhiteSpace(artCode))
            {
                noticeUrl = $"https://data.eastmoney.com/notices/detail/{symbol}/{artCode}.html";
            }

            news.Add(new NewsItem
            {
                Title = title,
                Summary = summary,
                Content = content,
                Url = noticeUrl,
                Source = "东方财富公告",
                PublishTime = pubTime,
                Sentiment = "neutral"
            });
        }
        return news;
    }

    public async Task<string> GetIndustryNameAsync(string symbol)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&fields=f127";
        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return "";
        return data.TryGetProperty("f127", out var f127) ? (f127.GetString() ?? "") : "";
    }

    public async Task<List<string>> GetIndustryPeerSymbolsAsync(string symbol, int maxPeers = 12)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/slist/get?secid={secid}&spt=2&ut=bd1d9ddb04089700cf9c27f6f7426281&fltt=2&invt=2&pn=1&pz=50&fields=f12,f14,f3";
        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return new List<string>();
        if (!data.TryGetProperty("diff", out var diff) || diff.ValueKind != JsonValueKind.Object)
            return new List<string>();

        var peers = new List<string>();
        foreach (var kv in diff.EnumerateObject())
        {
            var item = kv.Value;
            var code = item.TryGetProperty("f12", out var f12) ? f12.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(code) || code == symbol) continue;
            if (code.Length != 6 || !code.All(char.IsDigit)) continue;
            peers.Add(code);
            if (peers.Count >= maxPeers) break;
        }
        return peers;
    }

    public async Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20)
    {
        var secid = GetSecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/fflow/kline/get?" +
                  $"secid={secid}&" +
                  "fields1=f1,f2,f3,f7&" +
                  "fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61,f62,f63&" +
                  "ut=b2884a393a59ad64002292a3e90d46a5&" +
                  "klt=101&" +
                  $"lmt={Math.Max(1, days)}";
        _logger.LogInformation("东方财富: 获取 {Symbol} 资金流向 {Days}天", symbol, days);

        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return new List<CapitalFlowItem>();
        if (!data.TryGetProperty("klines", out var klines) || klines.ValueKind != JsonValueKind.Array)
            return new List<CapitalFlowItem>();

        var result = new List<CapitalFlowItem>();
        foreach (var line in klines.EnumerateArray())
        {
            var str = line.GetString();
            if (string.IsNullOrEmpty(str)) continue;
            var parts = str.Split(',');
            if (parts.Length < 6) continue;

            result.Add(new CapitalFlowItem
            {
                Symbol = symbol,
                Date = DateTime.Parse(parts[0]),
                MainForce = ParseDecimal(parts[1]),
                SmallOrder = ParseDecimal(parts[2]),
                MediumOrder = ParseDecimal(parts[3]),
                LargeOrder = ParseDecimal(parts[4]),
                SuperLargeOrder = ParseDecimal(parts[5]),
                Source = "EastMoney",
                IsApproximate = false,
                IsDataAvailable = true,
                DataNote = "真实资金流数据（东方财富 fflow/kline）。"
            });
        }
        return result;
    }

    // ─── 内部辅助 ───────────────────────────────────────────

    private async Task<string> ThrottledGetAsync(string url)
    {
        // 先查缓存
        var cached = await _cache.GetAsync(url);
        if (cached != null) return cached;

        await _throttle.WaitAsync();
        try
        {
            string result;
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    result = await _http.GetStringAsync(url);
                    // 缓存结果 (行情60s, 其他5min)
                    var ttl = url.Contains("/stock/get") ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(5);
                    await _cache.SetAsync(url, result, ttl);
                    return result;
                }
                catch (HttpRequestException) when (retry < 2)
                {
                    _logger.LogDebug("API请求失败, {Delay}ms后重试", (retry + 1) * 1000);
                    await Task.Delay((retry + 1) * 1000);
                }
            }
            result = await _http.GetStringAsync(url);
            return result;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static string GetSecId(string symbol)
    {
        if (symbol.StartsWith("6")) return $"1.{symbol}";
        return $"0.{symbol}";
    }

    private static decimal GetDecimalProp(JsonElement elem, string prop)
    {
        if (elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetDecimal();
        return 0;
    }

    private static decimal ParseDecimal(string raw)
    {
        return decimal.TryParse(raw, out var d) ? d : 0;
    }

    private async Task<(string Name, decimal PE, decimal PB, decimal MarketCap)> GetQuoteSummaryAsync(
        string symbol, string secid)
    {
        var url = $"https://push2.eastmoney.com/api/qt/stock/get?" +
                  $"secid={secid}&ut=bd1d9ddb04089700cf9c27f6f7426281&fields=f43,f58,f84,f85,f104,f105,f116,f117,f162,f167";
        var json = await ThrottledGetAsync(url);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null)
            return (symbol, 0, 0, 0);

        var price = GetDecimalProp(data, "f43") / 100m;
        var marketCap = GetDecimalProp(data, "f116");
        if (marketCap == 0)
        {
            var shares = GetDecimalProp(data, "f84");
            if (shares == 0) shares = GetDecimalProp(data, "f85");
            if (shares > 0 && price > 0) marketCap = shares * price;
        }

        var pe = GetDecimalProp(data, "f162") / 100m;
        var pb = GetDecimalProp(data, "f167") / 100m;
        if (pe == 0) pe = GetDecimalProp(data, "f104");
        if (pb == 0) pb = GetDecimalProp(data, "f105");

        return (
            data.GetProperty("f58").GetString() ?? symbol,
            pe,
            pb,
            marketCap
        );
    }


    private async Task<(decimal ROE, decimal ROA, decimal GrossMargin, decimal NetMargin,
        decimal RevenueGrowth, decimal ProfitGrowth, decimal DebtRatio, DateTime ReportDate)>
        GetFinancialDataAsync(string symbol)
    {
        var filter = $"(SECURITY_CODE=\"{symbol}\")";
        var url = "https://datacenter.eastmoney.com/securities/api/data/get?" +
                  "type=RPT_F10_FINANCE_MAINFINADATA&" +
                  "sty=ROEJQ,XSMLL,XSJLL,TOTALOPERATEREVETZ,PARENTNETPROFITTZ,ZCFZL,SECURITY_NAME_ABBR,REPORT_DATE&" +
                  $"filter={Uri.EscapeDataString(filter)}&" +
                  "p=1&ps=1&sr=-1&st=REPORT_DATE&source=HSF10";

        using var doc = JsonDocument.Parse(await ThrottledGetAsync(url));
        var result = doc.RootElement.GetProperty("result");
        if (result.ValueKind == JsonValueKind.Null || !result.TryGetProperty("data", out var dataArr))
            return (0, 0, 0, 0, 0, 0, 0, DateTime.Now);

        if (dataArr.GetArrayLength() == 0)
            return (0, 0, 0, 0, 0, 0, 0, DateTime.Now);

        var item = dataArr[0];
        var reportDate = item.TryGetProperty("REPORT_DATE", out var rd) && rd.GetString() is string ds
            ? (DateTime.TryParse(ds, out var dt) ? dt : DateTime.Now)
            : DateTime.Now;

        return (
            ROE: GetDecimalProp(item, "ROEJQ"),
            ROA: GetDecimalProp(item, "ROEJQ") * 0.3m,
            GrossMargin: GetDecimalProp(item, "XSMLL"),
            NetMargin: GetDecimalProp(item, "XSJLL"),
            RevenueGrowth: GetDecimalProp(item, "TOTALOPERATEREVETZ"),
            ProfitGrowth: GetDecimalProp(item, "PARENTNETPROFITTZ"),
            DebtRatio: GetDecimalProp(item, "ZCFZL"),
            ReportDate: reportDate
        );
    }

    private async Task<List<(decimal ROE, decimal ROA, decimal GrossMargin, decimal NetMargin,
        decimal RevenueGrowth, decimal ProfitGrowth, decimal DebtRatio, DateTime ReportDate)>>
        GetFinancialDataHistoryAsync(string symbol, int maxReports)
    {
        var filter = $"(SECURITY_CODE=\"{symbol}\")";
        var url = "https://datacenter.eastmoney.com/securities/api/data/get?" +
                  "type=RPT_F10_FINANCE_MAINFINADATA&" +
                  "sty=ROEJQ,XSMLL,XSJLL,TOTALOPERATEREVETZ,PARENTNETPROFITTZ,ZCFZL,SECURITY_NAME_ABBR,REPORT_DATE&" +
                  $"filter={Uri.EscapeDataString(filter)}&" +
                  $"p=1&ps={Math.Max(1, maxReports)}&sr=-1&st=REPORT_DATE&source=HSF10";

        using var doc = JsonDocument.Parse(await ThrottledGetAsync(url));
        var result = new List<(decimal, decimal, decimal, decimal, decimal, decimal, decimal, DateTime)>();
        if (!doc.RootElement.TryGetProperty("result", out var rs) || rs.ValueKind == JsonValueKind.Null) return result;
        if (!rs.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in arr.EnumerateArray())
        {
            var reportDate = item.TryGetProperty("REPORT_DATE", out var rd) && rd.GetString() is string ds
                ? (DateTime.TryParse(ds, out var dt) ? dt : DateTime.Now)
                : DateTime.Now;
            result.Add((
                ROE: GetDecimalProp(item, "ROEJQ"),
                ROA: GetDecimalProp(item, "ROEJQ") * 0.3m,
                GrossMargin: GetDecimalProp(item, "XSMLL"),
                NetMargin: GetDecimalProp(item, "XSJLL"),
                RevenueGrowth: GetDecimalProp(item, "TOTALOPERATEREVETZ"),
                ProfitGrowth: GetDecimalProp(item, "PARENTNETPROFITTZ"),
                DebtRatio: GetDecimalProp(item, "ZCFZL"),
                ReportDate: reportDate
            ));
        }
        return result;
    }
}
