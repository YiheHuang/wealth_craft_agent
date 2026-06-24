namespace InvestAgent.Core.Models;

/// <summary>
/// 历史形态匹配结果。
/// 包含匹配到的历史案例及其相似度评分和匹配原因。
/// </summary>
public class HistoricalPatternMatch
{
    /// <summary>匹配到的历史案例</summary>
    public HistoricalPatternCase Case { get; set; } = new();

    /// <summary>相似度评分（越高越相似）</summary>
    public double SimilarityScore { get; set; }

    /// <summary>被判定为相似的具体原因列表</summary>
    public List<string> MatchReasons { get; set; } = new();
}
