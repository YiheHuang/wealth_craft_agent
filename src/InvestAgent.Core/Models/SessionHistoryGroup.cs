namespace InvestAgent.Core.Models;

/// <summary>
/// 按股票分组的历史会话视图模型。
/// 将同一股票的所有分析会话聚合在一起，用于历史列表的分组展示。
/// </summary>
public class SessionHistoryGroup
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称</summary>
    public string StockName { get; set; } = "";

    /// <summary>该股票的所有历史分析会话记录</summary>
    public List<AnalysisSessionRecord> Sessions { get; set; } = new();
}
