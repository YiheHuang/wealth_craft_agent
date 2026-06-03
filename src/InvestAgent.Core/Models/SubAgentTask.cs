namespace InvestAgent.Core.Models;

public class SubAgentTask
{
    public string Agent { get; set; } = "";
    public string Instruction { get; set; } = "";
    public bool UseChanTheory { get; set; }
    public int? DailyDays { get; set; }
    public int? MonthlyMonths { get; set; }
    public int? FinancialYears { get; set; }
    public int? NewsMonths { get; set; }
    public string NewsSentimentFilter { get; set; } = "all";
}
