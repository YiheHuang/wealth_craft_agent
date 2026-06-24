namespace InvestAgent.Core.Models;

/// <summary>
/// 股票实时行情数据模型。
/// 包含最新价、涨跌幅、成交量、换手率等核心行情字段。
/// </summary>
public class StockQuote
{
    /// <summary>股票代码，如 600519、AAPL</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称，如 贵州茅台</summary>
    public string Name { get; set; } = "";

    /// <summary>最新价格</summary>
    public decimal Price { get; set; }

    /// <summary>涨跌幅（百分比）</summary>
    public decimal ChangePercent { get; set; }

    /// <summary>涨跌额</summary>
    public decimal ChangeAmount { get; set; }

    /// <summary>成交量（股）</summary>
    public decimal Volume { get; set; }

    /// <summary>成交额（元）</summary>
    public decimal Turnover { get; set; }

    /// <summary>当日最高价</summary>
    public decimal High { get; set; }

    /// <summary>当日最低价</summary>
    public decimal Low { get; set; }

    /// <summary>开盘价</summary>
    public decimal Open { get; set; }

    /// <summary>前收盘价</summary>
    public decimal PreClose { get; set; }

    /// <summary>数据更新时间</summary>
    public DateTime UpdateTime { get; set; } = DateTime.Now;
}
