namespace InvestAgent.Core.Models;

/// <summary>
/// 历史案例的后续实际走势结果。
/// 记录观察窗口结束之后的实际市场表现数据，用于验证形态判断的准确性。
/// </summary>
public class HistoricalPatternOutcome
{
    /// <summary>窗口结束后 20 个交易日的收益率（%）</summary>
    public double Return20dPct { get; set; }

    /// <summary>窗口结束后 60 个交易日的收益率（%）</summary>
    public double Return60dPct { get; set; }

    /// <summary>窗口结束后 120 个交易日的收益率（%）</summary>
    public double Return120dPct { get; set; }

    /// <summary>窗口结束后 60 个交易日内的最大回撤（%）</summary>
    public double MaxDrawdownNext60dPct { get; set; }

    /// <summary>窗口结束后 60 个交易日内的最大反弹幅度（%）</summary>
    public double MaxReboundNext60dPct { get; set; }

    /// <summary>窗口结束后 60 个交易日内是否创出新低</summary>
    public bool NewLowWithin60d { get; set; }
}
