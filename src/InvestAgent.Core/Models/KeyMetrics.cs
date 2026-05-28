namespace InvestAgent.Core.Models;

public class KeyMetrics
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PE { get; set; }
    public decimal PB { get; set; }
    public decimal ROE { get; set; }
    public decimal ROA { get; set; }
    public decimal GrossMargin { get; set; }
    public decimal NetMargin { get; set; }
    public decimal RevenueGrowth { get; set; }
    public decimal ProfitGrowth { get; set; }
    public decimal DebtRatio { get; set; }
    public decimal MarketCap { get; set; }
    public DateTime ReportDate { get; set; }
}
