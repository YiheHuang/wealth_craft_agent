namespace InvestAgent.Core.Models;

public class SessionStatePatch
{
    public int? DailyDays { get; set; }
    public int? MonthlyMonths { get; set; }
    public int? FinancialYears { get; set; }
    public int? NewsMonths { get; set; }
    public string? NewsSentimentFilter { get; set; }

    public string? MainBusiness { get; set; }
    public string? AgentBResult { get; set; }
    public string? AgentCResult { get; set; }
    public string? AgentDResult { get; set; }
    public string? FinalRiskAdvice { get; set; }
    public string? InitialAnalysisResponse { get; set; }
    public string? FinalResponse { get; set; }
    public string? StockName { get; set; }

    public List<StockKLine>? DailyKLines { get; set; }
    public List<StockKLine>? MonthlyKLines { get; set; }
    public List<NewsItem>? CompanyNews { get; set; }
    public List<NewsItem>? IndustryNews { get; set; }
    public List<KeyMetrics>? FinancialHistory { get; set; }

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
