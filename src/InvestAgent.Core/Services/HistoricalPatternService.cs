using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

public interface IHistoricalPatternService
{
    HistoricalPatternSearchResult SearchSimilarPatterns(
        string symbol,
        IReadOnlyList<StockKLine> currentKLines,
        string query,
        int topN = 8);
}

/// <summary>
/// 历史形态相似度搜索服务的实现。
/// 从当前K线提取多维特征向量，与案例库进行加权距离比较，
/// 输出最相似的历史案例及后续走势统计。
/// </summary>
public class HistoricalPatternService : IHistoricalPatternService
{
    private readonly ILogger<HistoricalPatternService> _logger;
    private readonly Lazy<List<HistoricalPatternCase>> _cases;

    public HistoricalPatternService(ILogger<HistoricalPatternService> logger)
    {
        _logger = logger;
        _cases = new Lazy<List<HistoricalPatternCase>>(LoadCases);
    }

    public HistoricalPatternSearchResult SearchSimilarPatterns(
        string symbol,
        IReadOnlyList<StockKLine> currentKLines,
        string query,
        int topN = 8)
    {
        var result = new HistoricalPatternSearchResult
        {
            TotalCaseCount = _cases.Value.Count
        };

        var ordered = currentKLines
            .Where(x => x.Close > 0)
            .OrderBy(x => x.Date)
            .ToList();
        if (ordered.Count < 45)
        {
            result.DataNote = "当前K线数量不足，至少需要45个交易日才能进行历史相似走势检索。";
            return result;
        }

        result.CurrentFeatures = ExtractFeatures(ordered.TakeLast(Math.Min(90, ordered.Count)).ToList());
        var currentCase = _cases.Value.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var currentIndustry = currentCase?.Industry ?? "";
        var queryTokens = BuildQueryTokens(query);

        result.Matches = _cases.Value
            .Select(item => BuildMatch(item, result.CurrentFeatures, symbol, currentIndustry, queryTokens))
            .OrderByDescending(x => x.SimilarityScore)
            .ThenBy(x => string.Equals(x.Case.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(Math.Max(1, topN))
            .ToList();

        result.MatchedCaseCount = result.Matches.Count;
        result.OutcomeStats = BuildOutcomeStats(result.Matches);
        if (result.TotalCaseCount == 0)
            result.DataNote = "未找到历史相似走势案例库，请先运行 scripts/Update-HistoricalPatternLibrary.ps1 生成 docs/historical_patterns/cases.json。";
        else if (result.MatchedCaseCount == 0)
            result.DataNote = "案例库已加载，但没有找到可比较的历史片段。";
        else
            result.DataNote = "历史相似走势仅表示结构相近，不构成预测；后续表现统计用于风险分布参考。";

        return result;
    }

    private HistoricalPatternMatch BuildMatch(
        HistoricalPatternCase item,
        HistoricalPatternFeatureVector current,
        string symbol,
        string currentIndustry,
        HashSet<string> queryTokens)
    {
        var distance = 0.0;
        distance += ScaledDistance(current.ReturnPct, item.Features.ReturnPct, 25) * 2.0;
        distance += ScaledDistance(current.MaxDrawdownPct, item.Features.MaxDrawdownPct, 30) * 2.0;
        distance += ScaledDistance(current.VolatilityPct, item.Features.VolatilityPct, 6);
        distance += ScaledDistance(current.VolumeRatio20d, item.Features.VolumeRatio20d, 2.5);
        distance += ScaledDistance(current.VolumeTrendPct, item.Features.VolumeTrendPct, 80);
        distance += ScaledDistance(current.Ma20SlopePct, item.Features.Ma20SlopePct, 6);
        distance += ScaledDistance(current.Ma60SlopePct, item.Features.Ma60SlopePct, 6);
        distance += ScaledDistance(current.Rsi14, item.Features.Rsi14, 45);
        distance += ScaledDistance(current.CloseNearLowPct, item.Features.CloseNearLowPct, 70);
        distance += current.BreakPreviousLow == item.Features.BreakPreviousLow ? 0 : 0.7;
        distance += string.Equals(current.MaArrangement, item.Features.MaArrangement, StringComparison.OrdinalIgnoreCase) ? 0 : 0.8;
        distance += string.Equals(current.MacdState, item.Features.MacdState, StringComparison.OrdinalIgnoreCase) ? 0 : 0.5;

        var score = 100 / (1 + distance);
        var reasons = new List<string>();

        if (string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
            reasons.Add("同一股票历史片段");
        }
        else if (!string.IsNullOrWhiteSpace(currentIndustry) &&
                 string.Equals(item.Industry, currentIndustry, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
            reasons.Add($"同行业案例：{item.Industry}");
        }

        var queryBoost = 0;
        foreach (var token in queryTokens)
        {
            if (item.PatternLabels.Any(x => x.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                item.PatternType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                item.MarketRegime.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                queryBoost++;
            }
        }
        if (queryBoost > 0)
        {
            score += Math.Min(8, queryBoost * 2);
            reasons.Add("与用户追问关键词匹配");
        }

        if (Math.Abs(current.ReturnPct - item.Features.ReturnPct) <= 5)
            reasons.Add("区间涨跌幅接近");
        if (Math.Abs(current.MaxDrawdownPct - item.Features.MaxDrawdownPct) <= 6)
            reasons.Add("最大回撤接近");
        if (current.BreakPreviousLow == item.Features.BreakPreviousLow)
            reasons.Add(item.Features.BreakPreviousLow ? "同样存在跌破前低特征" : "同样未明显跌破前低");
        if (string.Equals(current.MaArrangement, item.Features.MaArrangement, StringComparison.OrdinalIgnoreCase))
            reasons.Add($"均线结构相近：{current.MaArrangement}");

        return new HistoricalPatternMatch
        {
            Case = item,
            SimilarityScore = Math.Round(score, 2),
            MatchReasons = reasons.Distinct().Take(5).ToList()
        };
    }

    public static HistoricalPatternFeatureVector ExtractFeatures(IReadOnlyList<StockKLine> source)
    {
        var ordered = source
            .Where(x => x.Close > 0)
            .OrderBy(x => x.Date)
            .ToList();
        if (ordered.Count == 0)
            return new HistoricalPatternFeatureVector();

        var closes = ordered.Select(x => (double)x.Close).ToList();
        var volumes = ordered.Select(x => (double)Math.Max(0, x.Volume)).ToList();
        var first = closes.First();
        var last = closes.Last();
        var high = ordered.Max(x => (double)x.High);
        var low = ordered.Min(x => (double)x.Low);
        var firstVolume = Average(volumes.Take(Math.Min(20, volumes.Count)));
        var lastVolume = Average(volumes.TakeLast(Math.Min(20, volumes.Count)));
        var priorVolume = volumes.Count >= 40
            ? Average(volumes.Skip(Math.Max(0, volumes.Count - 40)).Take(20))
            : firstVolume;

        var ma20 = MovingAverage(closes, 20);
        var ma60 = MovingAverage(closes, 60);
        var currentMa20 = ma20.LastOrDefault();
        var currentMa60 = ma60.LastOrDefault();
        var ma20Past = ma20.Count > 20 ? ma20[^21] : ma20.FirstOrDefault();
        var ma60Past = ma60.Count > 20 ? ma60[^21] : ma60.FirstOrDefault();
        var maArrangement = "mixed";
        if (currentMa20 > 0 && currentMa60 > 0)
        {
            if (last > currentMa20 && currentMa20 > currentMa60)
                maArrangement = "bullish";
            else if (last < currentMa20 && currentMa20 < currentMa60)
                maArrangement = "bearish";
        }

        return new HistoricalPatternFeatureVector
        {
            ReturnPct = Round(Pct(last, first)),
            MaxDrawdownPct = Round(CalculateMaxDrawdown(closes)),
            VolatilityPct = Round(CalculateVolatility(closes)),
            VolumeRatio20d = Round(priorVolume <= 0 ? 1 : lastVolume / priorVolume),
            VolumeTrendPct = Round(firstVolume <= 0 ? 0 : (lastVolume - firstVolume) / firstVolume * 100),
            Ma20SlopePct = Round(ma20Past <= 0 ? 0 : (currentMa20 - ma20Past) / ma20Past * 100),
            Ma60SlopePct = Round(ma60Past <= 0 ? 0 : (currentMa60 - ma60Past) / ma60Past * 100),
            MaArrangement = maArrangement,
            MacdState = CalculateMacdState(closes),
            Rsi14 = Round(CalculateRsi(closes, 14)),
            CloseNearLowPct = Round(high <= low ? 50 : (last - low) / (high - low) * 100),
            BreakPreviousLow = IsBreakPreviousLow(ordered),
            UpDays = CountUpDays(closes),
            DownDays = Math.Max(0, closes.Count - 1 - CountUpDays(closes))
        };
    }

    private List<HistoricalPatternCase> LoadCases()
    {
        try
        {
            var path = ResolveCasesPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _logger.LogWarning("未找到历史相似走势案例库: {Path}", path);
                return new List<HistoricalPatternCase>();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("cases", out var casesElement) ||
                casesElement.ValueKind != JsonValueKind.Array)
            {
                return new List<HistoricalPatternCase>();
            }

            return JsonSerializer.Deserialize<List<HistoricalPatternCase>>(
                       casesElement.GetRawText(),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                   new List<HistoricalPatternCase>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载历史相似走势案例库失败");
            return new List<HistoricalPatternCase>();
        }
    }

    private static HistoricalPatternOutcomeStats BuildOutcomeStats(IReadOnlyList<HistoricalPatternMatch> matches)
    {
        var outcomes = matches.Select(x => x.Case.FutureOutcome).ToList();
        if (outcomes.Count == 0)
            return new HistoricalPatternOutcomeStats();

        return new HistoricalPatternOutcomeStats
        {
            SampleSize = outcomes.Count,
            Up20dRatePct = Round(outcomes.Count(x => x.Return20dPct > 0) * 100.0 / outcomes.Count),
            Up60dRatePct = Round(outcomes.Count(x => x.Return60dPct > 0) * 100.0 / outcomes.Count),
            Up120dRatePct = Round(outcomes.Count(x => x.Return120dPct > 0) * 100.0 / outcomes.Count),
            NewLowWithin60dRatePct = Round(outcomes.Count(x => x.NewLowWithin60d) * 100.0 / outcomes.Count),
            MedianReturn20dPct = Round(Median(outcomes.Select(x => x.Return20dPct))),
            MedianReturn60dPct = Round(Median(outcomes.Select(x => x.Return60dPct))),
            MedianReturn120dPct = Round(Median(outcomes.Select(x => x.Return120dPct))),
            MedianMaxDrawdownNext60dPct = Round(Median(outcomes.Select(x => x.MaxDrawdownNext60dPct))),
            PatternTypeCounts = matches
                .GroupBy(x => x.Case.PatternType)
                .OrderByDescending(x => x.Count())
                .ToDictionary(x => x.Key, x => x.Count())
        };
    }

    private static HashSet<string> BuildQueryTokens(string query)
    {
        var raw = query ?? "";
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in new[] { "底部", "阶段底部", "下跌中继", "相似", "历史", "白酒", "回撤", "反弹", "破位", "支撑", "长期", "持有" })
        {
            if (raw.Contains(token, StringComparison.OrdinalIgnoreCase))
                tokens.Add(token);
        }

        return tokens;
    }

    private static string? ResolveCasesPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "historical_patterns", "cases.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static double ScaledDistance(double left, double right, double scale)
    {
        if (scale <= 0)
            return 0;
        return Math.Min(4, Math.Abs(left - right) / scale);
    }

    private static double Pct(double current, double basis) => basis == 0 ? 0 : (current - basis) / basis * 100;

    private static double CalculateMaxDrawdown(IReadOnlyList<double> closes)
    {
        if (closes.Count == 0)
            return 0;

        var peak = closes[0];
        var maxDrawdown = 0.0;
        foreach (var close in closes)
        {
            if (close > peak)
                peak = close;
            if (peak > 0)
                maxDrawdown = Math.Min(maxDrawdown, (close - peak) / peak * 100);
        }
        return maxDrawdown;
    }

    private static double CalculateVolatility(IReadOnlyList<double> closes)
    {
        if (closes.Count < 2)
            return 0;
        var returns = new List<double>();
        for (var i = 1; i < closes.Count; i++)
        {
            if (closes[i - 1] != 0)
                returns.Add((closes[i] - closes[i - 1]) / closes[i - 1] * 100);
        }
        if (returns.Count == 0)
            return 0;
        var avg = returns.Average();
        return Math.Sqrt(returns.Sum(x => Math.Pow(x - avg, 2)) / returns.Count);
    }

    private static List<double> MovingAverage(IReadOnlyList<double> values, int period)
    {
        var result = new List<double>();
        for (var i = 0; i < values.Count; i++)
        {
            var start = Math.Max(0, i - period + 1);
            var count = i - start + 1;
            result.Add(values.Skip(start).Take(count).Average());
        }
        return result;
    }

    private static string CalculateMacdState(IReadOnlyList<double> closes)
    {
        if (closes.Count < 35)
            return "insufficient";

        var ema12 = Ema(closes, 12);
        var ema26 = Ema(closes, 26);
        var dif = ema12.Zip(ema26, (a, b) => a - b).ToList();
        var dea = Ema(dif, 9);
        var lastDif = dif.Last();
        var lastDea = dea.Last();
        if (lastDif >= 0 && lastDif >= lastDea) return "above_zero_strong";
        if (lastDif >= 0 && lastDif < lastDea) return "above_zero_fading";
        if (lastDif < 0 && lastDif >= lastDea) return "below_zero_repair";
        return "below_zero_weak";
    }

    private static List<double> Ema(IReadOnlyList<double> values, int period)
    {
        var result = new List<double>();
        if (values.Count == 0)
            return result;
        var multiplier = 2.0 / (period + 1);
        for (var i = 0; i < values.Count; i++)
        {
            if (i == 0)
                result.Add(values[i]);
            else
                result.Add((values[i] - result[i - 1]) * multiplier + result[i - 1]);
        }
        return result;
    }

    private static double CalculateRsi(IReadOnlyList<double> closes, int period)
    {
        if (closes.Count < period + 1)
            return 50;

        var gains = new List<double>();
        var losses = new List<double>();
        for (var i = closes.Count - period; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            gains.Add(Math.Max(0, diff));
            losses.Add(Math.Max(0, -diff));
        }
        var avgGain = gains.Average();
        var avgLoss = losses.Average();
        if (avgLoss == 0)
            return 100;
        var rs = avgGain / avgLoss;
        return 100 - 100 / (1 + rs);
    }

    private static bool IsBreakPreviousLow(IReadOnlyList<StockKLine> ordered)
    {
        if (ordered.Count < 35)
            return false;
        var lastClose = ordered.Last().Close;
        var previousWindow = ordered.Take(Math.Max(1, ordered.Count - 20)).ToList();
        return lastClose <= previousWindow.Min(x => x.Low);
    }

    private static int CountUpDays(IReadOnlyList<double> closes)
    {
        var count = 0;
        for (var i = 1; i < closes.Count; i++)
        {
            if (closes[i] >= closes[i - 1])
                count++;
        }
        return count;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(x => x).ToList();
        if (ordered.Count == 0)
            return 0;
        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2 : ordered[mid];
    }

    private static double Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    private static double Round(double value) => Math.Round(value, 2);
}
