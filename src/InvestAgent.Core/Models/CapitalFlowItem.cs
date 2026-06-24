namespace InvestAgent.Core.Models;

/// <summary>
/// 资金流向数据模型，记录单日的资金流入/流出情况。
/// 数据来源包括东方财富真实资金流和 Finnhub 内部人情绪近似估算。
/// </summary>
public class CapitalFlowItem
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>数据日期</summary>
    public DateTime Date { get; set; }

    /// <summary>数据来源标识（EastMoney / Finnhub 等）</summary>
    public string Source { get; set; } = "";

    /// <summary>是否为近似估算数据（非交易所原始口径）</summary>
    public bool IsApproximate { get; set; }

    /// <summary>数据是否可用（false 表示数据缺口）</summary>
    public bool IsDataAvailable { get; set; } = true;

    /// <summary>数据备注说明，用于解释数据不可用原因或口径差异</summary>
    public string DataNote { get; set; } = "";

    /// <summary>主力净流入（元）</summary>
    public decimal MainForce { get; set; }

    /// <summary>超大单净流入（元）</summary>
    public decimal SuperLargeOrder { get; set; }

    /// <summary>大单净流入（元）</summary>
    public decimal LargeOrder { get; set; }

    /// <summary>中单净流入（元）</summary>
    public decimal MediumOrder { get; set; }

    /// <summary>小单净流入（元）</summary>
    public decimal SmallOrder { get; set; }

    /// <summary>合计净流入（元），以主力净流入为代理指标</summary>
    public decimal TotalInflow => MainForce;

    /// <summary>显示友好的金额文字（自动转换为亿/万单位）</summary>
    public string TotalInflowText => TotalInflow switch
    {
        >= 100000000 or <= -100000000 => $"{TotalInflow / 100000000m:F2}亿",
        >= 10000 or <= -10000 => $"{TotalInflow / 10000m:F0}万",
        _ => $"{TotalInflow:F0}"
    };
}
