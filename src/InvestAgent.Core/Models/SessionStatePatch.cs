namespace InvestAgent.Core.Models;

/// <summary>
/// 会话状态增量补丁。
/// 用于子 Agent 执行完成后对会话状态进行部分更新。
/// 所有属性均为可空类型——非 null 值表示需要更新对应字段。
/// 通过 <see cref="ApplyTo"/> 方法将补丁合并到 <see cref="AnalysisSessionState"/>。
/// </summary>
public class SessionStatePatch
{
    // ── 参数补丁 ──────────────────────────────────────

    /// <summary>更新日K分析天数</summary>
    public int? DailyDays { get; set; }

    /// <summary>更新月K分析月数</summary>
    public int? MonthlyMonths { get; set; }

    /// <summary>更新财务分析年数</summary>
    public int? FinancialYears { get; set; }

    /// <summary>更新新闻分析月数</summary>
    public int? NewsMonths { get; set; }

    /// <summary>更新新闻情绪过滤</summary>
    public string? NewsSentimentFilter { get; set; }

    // ── 内容补丁 ──────────────────────────────────────

    /// <summary>更新主营业务描述</summary>
    public string? MainBusiness { get; set; }

    /// <summary>更新 Agent B 分析结果</summary>
    public string? AgentBResult { get; set; }

    /// <summary>更新 Agent C 分析结果</summary>
    public string? AgentCResult { get; set; }

    /// <summary>更新 Agent D 分析结果</summary>
    public string? AgentDResult { get; set; }

    /// <summary>更新风险建议</summary>
    public string? FinalRiskAdvice { get; set; }

    /// <summary>更新初始分析响应</summary>
    public string? InitialAnalysisResponse { get; set; }

    /// <summary>更新最终响应</summary>
    public string? FinalResponse { get; set; }

    /// <summary>更新股票名称</summary>
    public string? StockName { get; set; }

    // ── 数据补丁 ──────────────────────────────────────

    /// <summary>替换日K线数据</summary>
    public List<StockKLine>? DailyKLines { get; set; }

    /// <summary>替换月K线数据</summary>
    public List<StockKLine>? MonthlyKLines { get; set; }

    /// <summary>替换公司新闻列表</summary>
    public List<NewsItem>? CompanyNews { get; set; }

    /// <summary>替换行业新闻列表</summary>
    public List<NewsItem>? IndustryNews { get; set; }

    /// <summary>替换财务指标历史序列</summary>
    public List<KeyMetrics>? FinancialHistory { get; set; }

    /// <summary>
    /// 将补丁中的非空值逐一应用到目标状态对象。
    /// 应用完成后自动将 UpdatedAt 设为当前时间。
    /// </summary>
    /// <param name="state">目标会话状态对象</param>
    public void ApplyTo(AnalysisSessionState state)
    {
        if (DailyDays.HasValue) state.DailyDays = DailyDays.Value;
        if (MonthlyMonths.HasValue) state.MonthlyMonths = MonthlyMonths.Value;
        if (FinancialYears.HasValue) state.FinancialYears = FinancialYears.Value;
        if (NewsMonths.HasValue) state.NewsMonths = NewsMonths.Value;
        if (!string.IsNullOrWhiteSpace(NewsSentimentFilter)) state.NewsSentimentFilter = NewsSentimentFilter;
        if (!string.IsNullOrWhiteSpace(MainBusiness)) state.MainBusiness = MainBusiness;
        if (!string.IsNullOrWhiteSpace(AgentBResult)) state.AgentBResult = AgentBResult;
        if (!string.IsNullOrWhiteSpace(AgentCResult)) state.AgentCResult = AgentCResult;
        if (!string.IsNullOrWhiteSpace(AgentDResult)) state.AgentDResult = AgentDResult;
        if (!string.IsNullOrWhiteSpace(FinalRiskAdvice)) state.FinalRiskAdvice = FinalRiskAdvice;
        if (!string.IsNullOrWhiteSpace(InitialAnalysisResponse)) state.InitialAnalysisResponse = InitialAnalysisResponse;
        if (!string.IsNullOrWhiteSpace(FinalResponse)) state.FinalResponse = FinalResponse;
        if (!string.IsNullOrWhiteSpace(StockName)) state.StockName = StockName;
        if (DailyKLines is not null) state.DailyKLines = DailyKLines;
        if (MonthlyKLines is not null) state.MonthlyKLines = MonthlyKLines;
        if (CompanyNews is not null) state.CompanyNews = CompanyNews;
        if (IndustryNews is not null) state.IndustryNews = IndustryNews;
        if (FinancialHistory is not null) state.FinancialHistory = FinancialHistory;
        state.UpdatedAt = DateTime.Now;
    }
}
