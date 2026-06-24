using InvestAgent.Core.Models;

namespace InvestAgent.Core.Services;

/// <summary>
/// 股票数据服务统一接口。
/// 抽象了不同数据源（Yahoo Finance、东方财富、Alpha Vantage 等）的差异，
/// 提供行情、K线、财务、新闻、资金流等全维度数据访问能力。
/// 具体实现可通过 DI 容器按 <see cref="AgentOptions.DataSource"/> 切换。
/// </summary>
public interface IStockDataService
{
    /// <summary>获取股票实时行情（价格、涨跌幅、成交量等）</summary>
    Task<StockQuote?> GetCurrentPriceAsync(string symbol);

    /// <summary>获取日K线历史数据</summary>
    /// <param name="days">获取天数（默认 30）</param>
    Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30);

    /// <summary>获取月K线历史数据</summary>
    /// <param name="months">获取月数（默认 36）</param>
    Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36);

    /// <summary>根据关键词搜索股票（支持代码和名称模糊匹配）</summary>
    Task<List<StockQuote>> SearchStockAsync(string keyword);

    /// <summary>获取核心财务指标（PE/PB/ROE 等）</summary>
    Task<KeyMetrics?> GetKeyMetricsAsync(string symbol);

    /// <summary>获取财务指标历史序列（用于趋势分析）</summary>
    /// <param name="maxReports">最大报告数量（默认 4）</param>
    Task<List<KeyMetrics>> GetKeyMetricsHistoryAsync(string symbol, int maxReports = 4);

    /// <summary>获取公司主营业务描述</summary>
    Task<string> GetMainBusinessAsync(string symbol);

    /// <summary>获取最新新闻/公告列表</summary>
    /// <param name="count">获取条数（默认 5）</param>
    Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5);

    /// <summary>获取资金流向数据</summary>
    /// <param name="days">获取天数（默认 20）</param>
    Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20);

    /// <summary>数据源名称（用于 UI 展示）</summary>
    string SourceName { get; }
}
