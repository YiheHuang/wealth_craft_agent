namespace InvestAgent.Core.Models;

/// <summary>
/// 分析会话的摘要记录（轻量级）。
/// 用于历史会话列表展示，不包含完整状态数据。
/// </summary>
public class AnalysisSessionRecord
{
    /// <summary>会话唯一 ID（数据库自增）</summary>
    public long Id { get; set; }

    /// <summary>分析的股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称</summary>
    public string StockName { get; set; } = "";

    /// <summary>会话标题（用于历史列表展示）</summary>
    public string SessionTitle { get; set; } = "";

    /// <summary>会话创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>会话最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
