namespace InvestAgent.Core.Models;

public class HistoricalPatternFeatureVector
{
    public double ReturnPct { get; set; }
    public double MaxDrawdownPct { get; set; }
    public double VolatilityPct { get; set; }
    public double VolumeRatio20d { get; set; }
    public double VolumeTrendPct { get; set; }
    public double Ma20SlopePct { get; set; }
    public double Ma60SlopePct { get; set; }
    public string MaArrangement { get; set; } = "";
    public string MacdState { get; set; } = "";
    public double Rsi14 { get; set; }
    public double CloseNearLowPct { get; set; }
    public bool BreakPreviousLow { get; set; }
    public int UpDays { get; set; }
    public int DownDays { get; set; }
}
