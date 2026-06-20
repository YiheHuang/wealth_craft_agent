using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvestAgent.Tests;

public class HistoricalPatternServiceTests
{
    [Fact]
    public void ExtractFeatures_Should_Describe_Current_Window()
    {
        var klines = BuildDecliningKLines("600519", 90);

        var features = HistoricalPatternService.ExtractFeatures(klines);

        Assert.True(features.ReturnPct < 0);
        Assert.True(features.MaxDrawdownPct < 0);
        Assert.InRange(features.CloseNearLowPct, 0, 100);
        Assert.Equal(89, features.UpDays + features.DownDays);
        Assert.False(string.IsNullOrWhiteSpace(features.MacdState));
        Assert.False(string.IsNullOrWhiteSpace(features.MaArrangement));
    }

    [Fact]
    public void SearchSimilarPatterns_Should_Load_Local_Case_Library()
    {
        var service = new HistoricalPatternService(NullLogger<HistoricalPatternService>.Instance);
        var klines = BuildDecliningKLines("600519", 90);

        var result = service.SearchSimilarPatterns(
            "600519",
            klines,
            "历史相似走势 阶段底部 下跌中继 白酒 长期持有 风险分布",
            topN: 5);

        Assert.True(result.TotalCaseCount >= 200);
        Assert.Equal(5, result.MatchedCaseCount);
        Assert.Equal(5, result.OutcomeStats.SampleSize);
        Assert.True(result.Matches.All(x => x.SimilarityScore > 0));
        Assert.Contains(result.Matches, x => x.Case.Industry == "白酒");
        Assert.Contains("历史相似走势", result.DataNote);
    }

    private static List<StockKLine> BuildDecliningKLines(string symbol, int count)
    {
        var start = new DateTime(2024, 1, 2);
        var result = new List<StockKLine>();
        for (var i = 0; i < count; i++)
        {
            var close = 100m - i * 0.32m + (decimal)Math.Sin(i / 4.0) * 1.1m;
            var open = close + (i % 3 - 1) * 0.35m;
            var high = Math.Max(open, close) + 1.25m;
            var low = Math.Min(open, close) - 1.4m;
            result.Add(new StockKLine
            {
                Symbol = symbol,
                Date = start.AddDays(i),
                Open = decimal.Round(open, 2),
                High = decimal.Round(high, 2),
                Low = decimal.Round(low, 2),
                Close = decimal.Round(close, 2),
                Volume = 1_000_000 + i * 2_000 + (i % 7) * 15_000
            });
        }

        return result;
    }
}
