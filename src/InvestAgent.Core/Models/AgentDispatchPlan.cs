namespace InvestAgent.Core.Models;

/// <summary>
/// Agent 调度计划。
/// 由 Agent A 的调度器根据用户追问生成，决定委托哪些子 Agent 执行任务。
/// </summary>
public class AgentDispatchPlan
{
    /// <summary>
    /// 调度模式：
    /// "delegate" — 正常委托子 Agent 执行；
    /// "clarify" — 需要用户澄清意图；
    /// "reject_switch" — 拒绝切换股票（会话绑定单只股票）
    /// </summary>
    public string Mode { get; set; } = "delegate";

    /// <summary>面向用户的提示信息（clarify/reject 模式下使用）</summary>
    public string UserFacingMessage { get; set; } = "";

    /// <summary>分配给子 Agent 的任务列表</summary>
    public List<SubAgentTask> Tasks { get; set; } = new();
}
