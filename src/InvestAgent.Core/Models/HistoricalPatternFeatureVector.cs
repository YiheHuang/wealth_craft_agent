namespace InvestAgent.Core.Models;

/// <summary>
/// 历史形态特征向量。
/// 将一段K线走势抽象为多维量化特征，用于相似度计算。
/// 涵盖价格、成交量、均线、技术指标等多个维度的特征。
/// </summary>
public class HistoricalPatternFeatureVector
{
    /// <summary>区间涨跌幅（%）</summary>
    public double ReturnPct { get; set; }

    /// <summary>区间最大回撤（%）</summary>
    public double MaxDrawdownPct { get; set; }

    /// <summary>日收益率标准差（波动率，%）</summary>
    public double VolatilityPct { get; set; }

    /// <summary>近20日成交量与前期20日成交量之比</summary>
    public double VolumeRatio20d { get; set; }

    /// <summary>成交量趋势变化（%）</summary>
    public double VolumeTrendPct { get; set; }

    /// <summary>MA20 斜率（%，反映短期趋势方向与强度）</summary>
    public double Ma20SlopePct { get; set; }

    /// <summary>MA60 斜率（%，反映中期趋势方向与强度）</summary>
    public double Ma60SlopePct { get; set; }

    /// <summary>均线排列方式：bullish（多头）、bearish（空头）、mixed（交织）</summary>
    public string MaArrangement { get; set; } = "";

    /// <summary>MACD 状态分类（如 above_zero_strong、below_zero_weak 等）</summary>
    public string MacdState { get; set; } = "";

    /// <summary>14 日 RSI 值（0-100）</summary>
    public double Rsi14 { get; set; }

    /// <summary>收盘价在区间内的高低位置比例（%，100 = 接近最高，0 = 接近最低）</summary>
    public double CloseNearLowPct { get; set; }

    /// <summary>是否跌破了前20日最低点</summary>
    public bool BreakPreviousLow { get; set; }

    /// <summary>区间内上涨天数</summary>
    public int UpDays { get; set; }

    /// <summary>区间内下跌天数</summary>
    public int DownDays { get; set; }
}
