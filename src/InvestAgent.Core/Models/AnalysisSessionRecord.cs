namespace InvestAgent.Core.Models;

public class AnalysisSessionRecord
{
    public long Id { get; set; }
    public string Symbol { get; set; } = "";
    public string StockName { get; set; } = "";
    public string SessionTitle { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
