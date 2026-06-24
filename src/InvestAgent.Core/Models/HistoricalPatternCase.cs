namespace InvestAgent.Core.Models;

/// <summary>
/// 历史形态案例模型。
/// 记录历史上某只股票在某个时间窗口内的技术形态特征及其后续走势结果。
/// 用于与当前走势进行相似度比较，辅助判断当前市场结构。
/// </summary>
public class HistoricalPatternCase
{
    /// <summary>案例唯一标识符</summary>
    public string CaseId { get; set; } = "";

    /// <summary>案例标题</summary>
    public string Title { get; set; } = "";

    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称</summary>
    public string Name { get; set; } = "";

    /// <summary>所属行业</summary>
    public string Industry { get; set; } = "";

    /// <summary>主题/赛道</summary>
    public string Theme { get; set; } = "";

    /// <summary>当时所处的市场环境描述</summary>
    public string MarketRegime { get; set; } = "";

    /// <summary>观察窗口起始日期</summary>
    public DateTime WindowStart { get; set; }

    /// <summary>观察窗口结束日期</summary>
    public DateTime WindowEnd { get; set; }

    /// <summary>观察窗口天数</summary>
    public int WindowDays { get; set; }

    /// <summary>形态类型分类（如 "阶段底部"、"下跌中继" 等）</summary>
    public string PatternType { get; set; } = "";

    /// <summary>形态标签列表</summary>
    public List<string> PatternLabels { get; set; } = new();

    /// <summary>该阶段的技术结构摘要</summary>
    public string StructureSummary { get; set; } = "";

    /// <summary>成交量特征摘要</summary>
    public string VolumeSummary { get; set; } = "";

    /// <summary>当时对该走势的风险解读</summary>
    public string RiskInterpretation { get; set; } = "";

    /// <summary>该案例的核心教训</summary>
    public string Lesson { get; set; } = "";

    /// <summary>应避免的说法/结论（用于约束 Agent 输出）</summary>
    public List<string> AvoidSaying { get; set; } = new();

    /// <summary>该案例窗口的量化特征向量</summary>
    public HistoricalPatternFeatureVector Features { get; set; } = new();

    /// <summary>窗口之后的实际走势结果</summary>
    public HistoricalPatternOutcome FutureOutcome { get; set; } = new();

    /// <summary>数据来源</summary>
    public string DataSource { get; set; } = "";
}
