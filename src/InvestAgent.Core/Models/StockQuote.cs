namespace InvestAgent.Core.Models;

public class StockQuote
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal ChangeAmount { get; set; }
    public decimal Volume { get; set; }
    public decimal Turnover { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Open { get; set; }
    public decimal PreClose { get; set; }
    public DateTime UpdateTime { get; set; } = DateTime.Now;
}
