namespace InvestAgent.Core.Models;

public class HistoricalPatternOutcome
{
    public double Return20dPct { get; set; }
    public double Return60dPct { get; set; }
    public double Return120dPct { get; set; }
    public double MaxDrawdownNext60dPct { get; set; }
    public double MaxReboundNext60dPct { get; set; }
    public bool NewLowWithin60d { get; set; }
}
