namespace InvestAgent.Core.Models;

public class HistoricalPatternMatch
{
    public HistoricalPatternCase Case { get; set; } = new();
    public double SimilarityScore { get; set; }
    public List<string> MatchReasons { get; set; } = new();
}
