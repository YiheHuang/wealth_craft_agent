using InvestAgent.Core.Models;

namespace InvestAgent.Core.Services;

/// <summary>
/// 组合式股票数据服务（默认数据源）。
/// 实现 A 股优先策略：A 股使用东方财富 + Yahoo 互补，美股使用 Yahoo + AlphaVantage。
/// 对于 A 股的财务、新闻、搜索等操作优先走东方财富，Yahoo 作为补充。
/// 实现 <see cref="IStockDataService"/> 接口，通过 DI 容器根据配置切换。
/// </summary>
public class CompositeStockDataService : IStockDataService
{
    private readonly YahooFinanceStockService _yahoo;
    private readonly AlphaVantageNewsService _news;
    private readonly FinnhubCapitalFlowService _flow;
    private readonly EastMoneyStockService _eastMoney;

    public CompositeStockDataService(
        YahooFinanceStockService yahoo,
        AlphaVantageNewsService news,
        FinnhubCapitalFlowService flow,
        EastMoneyStockService eastMoney)
    {
        _yahoo = yahoo;
        _news = news;
        _flow = flow;
        _eastMoney = eastMoney;
    }

    public string SourceName => "AShareFirst(Yahoo+EastMoney+AlphaVantage)";

    // ── 行情和 K 线 —— 统一走 Yahoo ──────────────────────────

    public Task<StockQuote?> GetCurrentPriceAsync(string symbol) => _yahoo.GetCurrentPriceAsync(symbol);
    public Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30) => _yahoo.GetHistoricalPricesAsync(symbol, days);
    public Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36) => _yahoo.GetMonthlyKLineAsync(symbol, months);

    // ── 搜索 —— A 股优先东方财富 ────────────────────────────

    public async Task<List<StockQuote>> SearchStockAsync(string keyword)
    {
        var normalized = keyword?.Trim() ?? "";

        // A 股或中文关键词优先走东方财富
        if (IsAShare(normalized) || ContainsChinese(normalized))
        {
            try
            {
                var em = await _eastMoney.SearchStockAsync(normalized);
                if (em.Count > 0) return em;
            }
            catch { }
        }

        // 回退到 Yahoo
        try
        {
            var y = await _yahoo.SearchStockAsync(normalized);
            if (y.Count > 0) return y;
        }
        catch { }

        // 最终尝试东方财富
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            try { return await _eastMoney.SearchStockAsync(normalized); }
            catch { }
        }

        return new List<StockQuote>();
    }

    // ── 财务指标 —— A 股优先东方财富，Yahoo 补充 ──────────────

    public async Task<KeyMetrics?> GetKeyMetricsAsync(string symbol)
    {
        KeyMetrics? em = null;
        if (IsAShare(symbol))
        {
            try { em = await _eastMoney.GetKeyMetricsAsync(symbol); } catch { }
        }

        KeyMetrics? y = null;
        try { y = await _yahoo.GetKeyMetricsAsync(symbol); } catch { }

        // 以东方财富为主体，Yahoo 补充缺失字段
        if (em is null) return y;
        if (y is null) return em;
        if (em.PE == 0) em.PE = y.PE;
        if (em.PB == 0) em.PB = y.PB;
        if (em.MarketCap == 0) em.MarketCap = y.MarketCap;
        if (string.IsNullOrWhiteSpace(em.Name) || em.Name == symbol) em.Name = y.Name;
        return em;
    }

    public async Task<List<KeyMetrics>> GetKeyMetricsHistoryAsync(string symbol, int maxReports = 4)
    {
        if (IsAShare(symbol))
        {
            try
            {
                var em = await _eastMoney.GetKeyMetricsHistoryAsync(symbol, maxReports);
                if (em.Count > 0) return em;
            }
            catch { }
        }
        try { return await _yahoo.GetKeyMetricsHistoryAsync(symbol, maxReports); }
        catch { return new List<KeyMetrics>(); }
    }

    // ── 主营业务 —— A 股优先东方财富 ─────────────────────────

    public async Task<string> GetMainBusinessAsync(string symbol)
    {
        if (IsAShare(symbol))
        {
            var em = await SafeMainBusinessAsync(() => _eastMoney.GetMainBusinessAsync(symbol));
            if (!string.IsNullOrWhiteSpace(em)) return em;
        }
        return await SafeMainBusinessAsync(() => _yahoo.GetMainBusinessAsync(symbol));
    }

    // ── 新闻 —— A 股走东方财富 + 行业新闻聚合，美股走 AlphaVantage ──

    public async Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5)
    {
        var merged = new List<NewsItem>();
        var isAShare = IsAShare(symbol);

        if (isAShare)
        {
            // A 股：先拉公司公告
            var companyNews = await SafeNewsAsync(() => _eastMoney.GetLatestNewsAsync(symbol, count));
            foreach (var n in companyNews)
            {
                n.DataNote = string.IsNullOrWhiteSpace(n.DataNote) ? "公司新闻" : n.DataNote;
                if (string.IsNullOrWhiteSpace(n.Content)) n.Content = n.Summary;
            }
            merged.AddRange(companyNews);

            // 行业新闻：通过同行业股票聚合
            var industry = await SafeIndustryNameAsync(symbol);
            var peers = await SafeIndustryPeersAsync(symbol);
            var targetIndustryNews = Math.Max(count, 20);
            var peerTake = Math.Min(peers.Count, 12);
            var perPeerCount = Math.Max(2, (int)Math.Ceiling((double)targetIndustryNews / Math.Max(1, peerTake)));

            foreach (var peer in peers.Take(peerTake))
            {
                var peerNews = await SafeNewsAsync(() => _eastMoney.GetLatestNewsAsync(peer, perPeerCount));
                foreach (var n in peerNews)
                {
                    n.Source = string.IsNullOrWhiteSpace(industry) ? n.Source : $"{n.Source} | 行业:{industry}";
                    n.DataNote = $"行业新闻（同业:{peer}）";
                    if (string.IsNullOrWhiteSpace(n.Content)) n.Content = n.Summary;
                }
                merged.AddRange(peerNews);
            }
        }

        // A 股新闻不使用 Alpha Vantage（避免境外源噪声）
        if (!isAShare)
        {
            var avNews = await SafeNewsAsync(() => _news.GetLatestNewsAsync(symbol, Math.Max(8, count)));
            foreach (var n in avNews)
            {
                n.DataNote = string.IsNullOrWhiteSpace(n.DataNote) ? "公司新闻(AV)" : n.DataNote;
                if (string.IsNullOrWhiteSpace(n.Content)) n.Content = n.Summary;
            }
            merged.AddRange(avNews);
        }

        // 过滤无效条目并去重
        var filtered = merged
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .Where(x => !x.Title.Contains("解析失败"))
            .Where(x => !x.Summary.Contains("解析失败"))
            .Where(x => !x.Title.Contains("数据暂不可用"))
            .ToList();

        var dedup = filtered
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .GroupBy(x => $"{x.Title}|{x.PublishTime:yyyy-MM-dd}|{x.Source}|{x.DataNote}")
            .Select(g => g.OrderByDescending(x => x.IsDataAvailable).First())
            .OrderByDescending(x => x.PublishTime)
            .ToList();

        // 上限保护（避免行业新闻过多挤占公司新闻）
        var cap = Math.Max(60, count * 6);
        if (dedup.Count > cap) dedup = dedup.Take(cap).ToList();

        if (HasUsefulNews(dedup)) return dedup;

        // 完全无有效新闻时的降级占位
        return [new NewsItem
        {
            Title = "新闻数据暂不可用",
            Summary = $"标的 {symbol} 暂未获取到有效新闻/公告，建议结合K线与财务指标分析。",
            Content = $"标的 {symbol} 暂未获取到有效新闻/公告，建议结合K线与财务指标分析。",
            Source = "CompositeFallback",
            PublishTime = DateTime.Now,
            Sentiment = "neutral",
            IsDataAvailable = false,
            DataNote = "已尝试公司新闻与行业新闻。"
        }];
    }

    /// <summary>资金流已按产品要求移除，返回空列表</summary>
    public Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20)
    {
        return Task.FromResult(new List<CapitalFlowItem>());
    }

    // ── 内部辅助 ───────────────────────────────────────────

    private async Task<string> SafeIndustryNameAsync(string symbol)
    {
        try { return await _eastMoney.GetIndustryNameAsync(symbol); } catch { return ""; }
    }

    private async Task<List<string>> SafeIndustryPeersAsync(string symbol)
    {
        try { return await _eastMoney.GetIndustryPeerSymbolsAsync(symbol, 12); } catch { return new List<string>(); }
    }

    /// <summary>判断是否为 A 股代码（6 位纯数字）</summary>
    private static bool IsAShare(string symbol) => symbol.All(char.IsDigit) && symbol.Length == 6;

    /// <summary>判断字符串是否包含中文字符</summary>
    private static bool ContainsChinese(string value) => value.Any(c => c >= '一' && c <= '鿿');

    /// <summary>检查新闻列表中是否有可用条目</summary>
    private static bool HasUsefulNews(List<NewsItem>? list)
        => list is { Count: > 0 } && list.Any(x => x.IsDataAvailable && !string.IsNullOrWhiteSpace(x.Title));

    private static async Task<List<NewsItem>> SafeNewsAsync(Func<Task<List<NewsItem>>> loader)
    {
        try { return await loader(); } catch { return new List<NewsItem>(); }
    }

    private static async Task<string> SafeMainBusinessAsync(Func<Task<string>> loader)
    {
        try { return await loader(); } catch { return ""; }
    }
}
