namespace InvestAgent.Core.Models;

public class AnalysisSessionState
{
    public long SessionId { get; set; }
    public string Symbol { get; set; } = "";
    public string StockName { get; set; } = "";
    public string SessionTitle { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public int DailyDays { get; set; } = 90;
    public int MonthlyMonths { get; set; } = 12;
    public int FinancialYears { get; set; } = 1;
    public int NewsMonths { get; set; } = 3;
    public string NewsSentimentFilter { get; set; } = "all";

    public string MainBusiness { get; set; } = "";
    public string AgentBResult { get; set; } = "";
    public string AgentCResult { get; set; } = "";
    public string AgentDResult { get; set; } = "";
    public string FinalRiskAdvice { get; set; } = "";
    public string FinalResponse { get; set; } = "";

    public List<StockKLine> DailyKLines { get; set; } = new();
    public List<StockKLine> MonthlyKLines { get; set; } = new();
    public List<NewsItem> CompanyNews { get; set; } = new();
    public List<NewsItem> IndustryNews { get; set; } = new();
    public List<KeyMetrics> FinancialHistory { get; set; } = new();
}
