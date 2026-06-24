namespace InvestAgent.Core.Models;

/// <summary>
/// 分析会话的完整状态快照。
/// 持有整个分析会话中的所有数据——从参数配置到各类分析结果。
/// 是 Agent 系统的核心状态容器，贯穿整个分析生命周期。
/// </summary>
public class AnalysisSessionState
{
    /// <summary>会话 ID</summary>
    public long SessionId { get; set; }

    /// <summary>分析的股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称</summary>
    public string StockName { get; set; } = "";

    /// <summary>会话标题</summary>
    public string SessionTitle { get; set; } = "";

    /// <summary>会话创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // ── 参数配置 ──────────────────────────────────────

    /// <summary>日K线分析天数（默认 90）</summary>
    public int DailyDays { get; set; } = 90;

    /// <summary>月K线分析月数（默认 12）</summary>
    public int MonthlyMonths { get; set; } = 12;

    /// <summary>财务数据分析年数（默认 1）</summary>
    public int FinancialYears { get; set; } = 1;

    /// <summary>新闻分析月数（默认 3）</summary>
    public int NewsMonths { get; set; } = 3;

    /// <summary>新闻情绪过滤："all"（全部）、"positive"（仅积极）、"negative"（仅消极）</summary>
    public string NewsSentimentFilter { get; set; } = "all";

    // ── 分析结果 ──────────────────────────────────────

    /// <summary>公司主营业务描述（由 Agent A 综合生成）</summary>
    public string MainBusiness { get; set; } = "";

    /// <summary>Agent B（K线/技术分析）的输出结果</summary>
    public string AgentBResult { get; set; } = "";

    /// <summary>Agent C（新闻/情绪分析）的输出结果</summary>
    public string AgentCResult { get; set; } = "";

    /// <summary>Agent D（财务分析）的输出结果</summary>
    public string AgentDResult { get; set; } = "";

    /// <summary>最终风险提示与投资建议</summary>
    public string FinalRiskAdvice { get; set; } = "";

    /// <summary>初始全量分析响应（首次分析的总报告）</summary>
    public string InitialAnalysisResponse { get; set; } = "";

    /// <summary>最终响应（实时更新，在 UI 中显示的最新分析报告）</summary>
    public string FinalResponse { get; set; } = "";

    // ── 数据缓存 ──────────────────────────────────────

    /// <summary>缓存的日K线数据</summary>
    public List<StockKLine> DailyKLines { get; set; } = new();

    /// <summary>缓存的月K线数据</summary>
    public List<StockKLine> MonthlyKLines { get; set; } = new();

    /// <summary>公司相关新闻列表</summary>
    public List<NewsItem> CompanyNews { get; set; } = new();

    /// <summary>行业相关新闻列表</summary>
    public List<NewsItem> IndustryNews { get; set; } = new();

    /// <summary>财务指标历史序列</summary>
    public List<KeyMetrics> FinancialHistory { get; set; } = new();
}
