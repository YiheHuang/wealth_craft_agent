namespace InvestAgent.Core.Models;

/// <summary>
/// 子 Agent 任务定义。
/// 由 Agent A 的调度器生成，描述需要某个子 Agent 执行的具体分析任务。
/// </summary>
public class SubAgentTask
{
    /// <summary>目标 Agent 标识（"B"、"C"、"D"）</summary>
    public string Agent { get; set; } = "";

    /// <summary>任务指令文本，传递给子 Agent 的自然语言描述</summary>
    public string Instruction { get; set; } = "";

    /// <summary>是否为初始全量分析（首次分析 vs 会话内追问）</summary>
    public bool IsInitialAnalysis { get; set; }

    /// <summary>是否启用缠论分析模式</summary>
    public bool UseChanTheory { get; set; }

    /// <summary>指定日K线分析天数（null 表示使用默认值）</summary>
    public int? DailyDays { get; set; }

    /// <summary>指定月K线分析月数（null 表示使用默认值）</summary>
    public int? MonthlyMonths { get; set; }

    /// <summary>指定财务分析年数（null 表示使用默认值）</summary>
    public int? FinancialYears { get; set; }

    /// <summary>指定新闻分析月数（null 表示使用默认值）</summary>
    public int? NewsMonths { get; set; }

    /// <summary>指定新闻情绪过滤</summary>
    public string NewsSentimentFilter { get; set; } = "all";
}
