namespace InvestAgent.Core.Models;

public class HistoricalPatternSearchResult
{
    public HistoricalPatternFeatureVector CurrentFeatures { get; set; } = new();
    public int TotalCaseCount { get; set; }
    public int MatchedCaseCount { get; set; }
    public List<HistoricalPatternMatch> Matches { get; set; } = new();
    public HistoricalPatternOutcomeStats OutcomeStats { get; set; } = new();
    public string DataNote { get; set; } = "";
}

public class HistoricalPatternOutcomeStats
{
    public int SampleSize { get; set; }
    public double Up20dRatePct { get; set; }
    public double Up60dRatePct { get; set; }
    public double Up120dRatePct { get; set; }
    public double NewLowWithin60dRatePct { get; set; }
    public double MedianReturn20dPct { get; set; }
    public double MedianReturn60dPct { get; set; }
    public double MedianReturn120dPct { get; set; }
    public double MedianMaxDrawdownNext60dPct { get; set; }
    public Dictionary<string, int> PatternTypeCounts { get; set; } = new();
}
