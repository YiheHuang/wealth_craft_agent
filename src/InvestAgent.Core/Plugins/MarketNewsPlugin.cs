using System.ComponentModel;
using System.Text.Json;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

public class MarketNewsPlugin
{
    private readonly IStockDataService _stockService;
    private readonly IWorkingMemory _workingMemory;
    private readonly ILogger<MarketNewsPlugin> _logger;

    public MarketNewsPlugin(IStockDataService stockService, IWorkingMemory workingMemory, ILogger<MarketNewsPlugin> logger)
    {
        _stockService = stockService;
        _workingMemory = workingMemory;
        _logger = logger;
    }

    [KernelFunction("get_latest_news")]
    [Description("获取指定股票的最新相关新闻和公告。用于了解市场情绪和最新动态。每条约包含标题、摘要、来源、时间、情绪标签。")]
    [return: Description("新闻列表JSON")]
    public async Task<string> GetLatestNewsAsync(
        [Description("股票代码")] string symbol,
        [Description("获取条数，默认5条")] int count = 5)
    {
        _logger.LogInformation("调用 GetLatestNews: {Symbol}, {Count}条", symbol, count);
        var cacheKey = $"news:{symbol}:{count}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetLatestNewsAsync(symbol, count),
            TimeSpan.FromMinutes(5));

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_market_sentiment")]
    [Description("分析指定股票近期的市场情绪倾向，基于新闻标题和摘要中的关键词判断情绪偏正面/负面/中性。返回情绪评分(-1到1)和关键词。")]
    [return: Description("市场情绪分析JSON")]
    public async Task<string> GetMarketSentimentAsync(
        [Description("股票代码")] string symbol)
    {
        _logger.LogInformation("调用 GetMarketSentiment: {Symbol}", symbol);
        var news = await _stockService.GetLatestNewsAsync(symbol, 10);

        var positiveWords = new[] { "增长", "利好", "买入", "超预期", "突破", "创新高", "回购", "分红", "签约", "中标" };
        var negativeWords = new[] { "下跌", "亏损", "减持", "风险", "处罚", "诉讼", "退市", "爆雷", "下滑", "低于预期" };

        double score = 0;
        var keywords = new List<string>();

        foreach (var n in news)
        {
            if (n.Sentiment == "positive") score += 0.2;
            else if (n.Sentiment == "negative") score -= 0.2;

            foreach (var word in positiveWords)
                if (n.Title.Contains(word)) { score += 0.15; keywords.Add(word); }

            foreach (var word in negativeWords)
                if (n.Title.Contains(word)) { score -= 0.15; keywords.Add(word); }
        }

        score = Math.Clamp(score, -1, 1);
        var label = score > 0.3 ? "正面" : score < -0.3 ? "负面" : "中性";

        return JsonSerializer.Serialize(new
        {
            Symbol = symbol,
            SentimentScore = Math.Round(score, 2),
            SentimentLabel = label,
            Keywords = keywords.Distinct().ToList(),
            NewsCount = news.Count
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
