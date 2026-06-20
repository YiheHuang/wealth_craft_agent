using InvestAgent.Core.Models;

namespace InvestAgent.Core.Services;

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

    public Task<StockQuote?> GetCurrentPriceAsync(string symbol) => _yahoo.GetCurrentPriceAsync(symbol);
    public Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30) => _yahoo.GetHistoricalPricesAsync(symbol, days);
    public Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36) => _yahoo.GetMonthlyKLineAsync(symbol, months);
    public async Task<List<StockQuote>> SearchStockAsync(string keyword)
    {
        var normalized = keyword?.Trim() ?? "";
        if (IsAShare(normalized) || ContainsChinese(normalized))
        {
            try
            {
                var em = await _eastMoney.SearchStockAsync(normalized);
                if (em.Count > 0)
                    return em;
            }
            catch { }
        }

        try
        {
            var y = await _yahoo.SearchStockAsync(normalized);
            if (y.Count > 0)
                return y;
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            try
            {
                return await _eastMoney.SearchStockAsync(normalized);
            }
            catch { }
        }

        return new List<StockQuote>();
    }

    public async Task<KeyMetrics?> GetKeyMetricsAsync(string symbol)
    {
        KeyMetrics? em = null;
        if (IsAShare(symbol))
        {
            try { em = await _eastMoney.GetKeyMetricsAsync(symbol); } catch { }
        }

        KeyMetrics? y = null;
        try { y = await _yahoo.GetKeyMetricsAsync(symbol); } catch { }

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
        try
        {
            return await _yahoo.GetKeyMetricsHistoryAsync(symbol, maxReports);
        }
        catch
        {
            return new List<KeyMetrics>();
        }
    }

    public async Task<string> GetMainBusinessAsync(string symbol)
    {
        if (IsAShare(symbol))
        {
            var em = await SafeMainBusinessAsync(() => _eastMoney.GetMainBusinessAsync(symbol));
            if (!string.IsNullOrWhiteSpace(em)) return em;
        }
        var y = await SafeMainBusinessAsync(() => _yahoo.GetMainBusinessAsync(symbol));
        return y;
    }

    public async Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5)
    {
        var merged = new List<NewsItem>();
        var isAShare = IsAShare(symbol);

        if (isAShare)
        {
            var companyNews = await SafeNewsAsync(() => _eastMoney.GetLatestNewsAsync(symbol, count));
            foreach (var n in companyNews)
            {
                n.DataNote = string.IsNullOrWhiteSpace(n.DataNote) ? "公司新闻" : n.DataNote;
                if (string.IsNullOrWhiteSpace(n.Content)) n.Content = n.Summary;
            }
            merged.AddRange(companyNews);

            var industry = await SafeIndustryNameAsync(symbol);
            var peers = await SafeIndustryPeersAsync(symbol);
            // 行业新闻目标量：不少于公司新闻 count，并预留一定冗余
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

        // A股新闻不使用 Alpha Vantage，避免境外源噪声
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

        // 尽量保留全量，避免行业新闻挤占公司新闻
        var cap = Math.Max(60, count * 6);
        if (dedup.Count > cap)
            dedup = dedup.Take(cap).ToList();

        if (HasUsefulNews(dedup)) return dedup;

        return
        [
            new NewsItem
            {
                Title = "新闻数据暂不可用",
                Summary = $"标的 {symbol} 暂未获取到有效新闻/公告，建议结合K线与财务指标分析。",
                Content = $"标的 {symbol} 暂未获取到有效新闻/公告，建议结合K线与财务指标分析。",
                Source = "CompositeFallback",
                PublishTime = DateTime.Now,
                Sentiment = "neutral",
                IsDataAvailable = false,
                DataNote = "已尝试公司新闻与行业新闻。"
            }
        ];
    }

    public Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20)
    {
        // 已按产品要求删除资金流入流出功能。
        return Task.FromResult(new List<CapitalFlowItem>());
    }

    private async Task<string> SafeIndustryNameAsync(string symbol)
    {
        try { return await _eastMoney.GetIndustryNameAsync(symbol); } catch { return ""; }
    }

    private async Task<List<string>> SafeIndustryPeersAsync(string symbol)
    {
        try { return await _eastMoney.GetIndustryPeerSymbolsAsync(symbol, 12); } catch { return new List<string>(); }
    }

    private static bool IsAShare(string symbol) => symbol.All(char.IsDigit) && symbol.Length == 6;
    private static bool ContainsChinese(string value) => value.Any(c => c >= '\u4e00' && c <= '\u9fff');
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
