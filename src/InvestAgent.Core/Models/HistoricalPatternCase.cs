namespace InvestAgent.Core.Models;

public class HistoricalPatternCase
{
    public string CaseId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Theme { get; set; } = "";
    public string MarketRegime { get; set; } = "";
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int WindowDays { get; set; }
    public string PatternType { get; set; } = "";
    public List<string> PatternLabels { get; set; } = new();
    public string StructureSummary { get; set; } = "";
    public string VolumeSummary { get; set; } = "";
    public string RiskInterpretation { get; set; } = "";
    public string Lesson { get; set; } = "";
    public List<string> AvoidSaying { get; set; } = new();
    public HistoricalPatternFeatureVector Features { get; set; } = new();
    public HistoricalPatternOutcome FutureOutcome { get; set; } = new();
    public string DataSource { get; set; } = "";
}
