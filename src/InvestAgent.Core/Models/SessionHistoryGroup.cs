namespace InvestAgent.Core.Models;

public class SessionHistoryGroup
{
    public string Symbol { get; set; } = "";
    public string StockName { get; set; } = "";
    public List<AnalysisSessionRecord> Sessions { get; set; } = new();
}
