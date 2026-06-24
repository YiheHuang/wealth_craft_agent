namespace InvestAgent.Core.Models;

/// <summary>
/// K线数据模型，表示单个交易周期（日/周/月）的 OHLCV 数据。
/// 用于技术分析、图表渲染和历史走势回溯。
/// </summary>
public class StockKLine
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>K线日期</summary>
    public DateTime Date { get; set; }

    /// <summary>开盘价</summary>
    public decimal Open { get; set; }

    /// <summary>最高价</summary>
    public decimal High { get; set; }

    /// <summary>最低价</summary>
    public decimal Low { get; set; }

    /// <summary>收盘价</summary>
    public decimal Close { get; set; }

    /// <summary>成交量（股）</summary>
    public decimal Volume { get; set; }
}
