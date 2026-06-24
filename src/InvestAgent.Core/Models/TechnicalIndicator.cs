namespace InvestAgent.Core.Models;

/// <summary>
/// 移动平均线（MA）计算结果。
/// 包含均线值、价格位置关系以及金叉/死叉信号。
/// </summary>
public class MAResult
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>均线周期（如 5、10、20、60 日）</summary>
    public int Period { get; set; }

    /// <summary>计算得到的均线值</summary>
    public decimal MAValue { get; set; }

    /// <summary>当前最新价格</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>当前价格与均线的相对位置（"价格在均线上方" / "价格在均线下方"）</summary>
    public string Position { get; set; } = "";

    /// <summary>是否出现金叉信号（短期均线上穿长期均线）</summary>
    public bool GoldenCross { get; set; }

    /// <summary>是否出现死叉信号（短期均线下穿长期均线）</summary>
    public bool DeadCross { get; set; }
}

/// <summary>
/// 相对强弱指数（RSI）计算结果。
/// RSI 范围 0-100，通常 >70 为超买，<30 为超卖。
/// </summary>
public class RSIResult
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>RSI 计算周期（通常为 14 日）</summary>
    public int Period { get; set; }

    /// <summary>RSI 数值</summary>
    public decimal RSIValue { get; set; }

    /// <summary>
    /// 状态解读：超买（>70）、严重超买（>80）、中性偏强（50-70）、
    /// 中性偏弱（30-50）、超卖（20-30）、严重超卖（<20）
    /// </summary>
    public string Status { get; set; } = "";
}

/// <summary>
/// MACD 指标计算结果。
/// 包含 DIF（快线）、DEA（慢线）、MACD 柱以及金叉/死叉信号。
/// </summary>
public class MACDResult
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>DIF 值（EMA12 - EMA26），即快线</summary>
    public decimal DIF { get; set; }

    /// <summary>DEA 值（DIF 的 9 日 EMA），即慢线/信号线</summary>
    public decimal DEA { get; set; }

    /// <summary>MACD 柱值（2 × (DIF - DEA)），正值为多头、负值为空头</summary>
    public decimal MACDHistogram { get; set; }

    /// <summary>是否出现金叉信号（DIF 上穿 DEA）</summary>
    public bool GoldenCross { get; set; }

    /// <summary>是否出现死叉信号（DIF 下穿 DEA）</summary>
    public bool DeadCross { get; set; }
}

/// <summary>
/// 综合交易信号结果。
/// 基于 MA、RSI、MACD 三大指标的综合评分，给出买入/卖出/持有建议。
/// </summary>
public class TradingSignal
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>交易信号：买入、卖出、持有（偏多/偏空）</summary>
    public string Signal { get; set; } = "";

    /// <summary>信号置信度（0-100），越高表示信号越强</summary>
    public int Confidence { get; set; }

    /// <summary>产生该信号的具体原因列表</summary>
    public List<string> Reasons { get; set; } = new();
}
