namespace InvestAgent.Core.Models;

/// <summary>
/// 历史形态搜索结果聚合体。
/// 包含当前走势的特征向量、匹配的案例列表以及后续走势的统计信息。
/// </summary>
public class HistoricalPatternSearchResult
{
    /// <summary>当前走势窗口的量化特征向量</summary>
    public HistoricalPatternFeatureVector CurrentFeatures { get; set; } = new();

    /// <summary>案例库中的总案例数</summary>
    public int TotalCaseCount { get; set; }

    /// <summary>本次匹配到的案例数</summary>
    public int MatchedCaseCount { get; set; }

    /// <summary>匹配到的历史案例列表（按相似度降序）</summary>
    public List<HistoricalPatternMatch> Matches { get; set; } = new();

    /// <summary>匹配案例的后续走势聚合统计</summary>
    public HistoricalPatternOutcomeStats OutcomeStats { get; set; } = new();

    /// <summary>数据说明（如案例库为空、命中不足等提示）</summary>
    public string DataNote { get; set; } = "";
}

/// <summary>
/// 历史形态后续走势的聚合统计。
/// 将多个相似案例的后续表现进行统计汇总，用于风险分布参考。
/// </summary>
public class HistoricalPatternOutcomeStats
{
    /// <summary>统计样本数</summary>
    public int SampleSize { get; set; }

    /// <summary>20 日内上涨的概率（%）</summary>
    public double Up20dRatePct { get; set; }

    /// <summary>60 日内上涨的概率（%）</summary>
    public double Up60dRatePct { get; set; }

    /// <summary>120 日内上涨的概率（%）</summary>
    public double Up120dRatePct { get; set; }

    /// <summary>60 日内再次创新低的概率（%）</summary>
    public double NewLowWithin60dRatePct { get; set; }

    /// <summary>20 日收益中位数（%）</summary>
    public double MedianReturn20dPct { get; set; }

    /// <summary>60 日收益中位数（%）</summary>
    public double MedianReturn60dPct { get; set; }

    /// <summary>120 日收益中位数（%）</summary>
    public double MedianReturn120dPct { get; set; }

    /// <summary>60 日内最大回撤中位数（%）</summary>
    public double MedianMaxDrawdownNext60dPct { get; set; }

    /// <summary>匹配案例的类型分布（如 "阶段底部": 5, "下跌中继": 3）</summary>
    public Dictionary<string, int> PatternTypeCounts { get; set; } = new();
}
