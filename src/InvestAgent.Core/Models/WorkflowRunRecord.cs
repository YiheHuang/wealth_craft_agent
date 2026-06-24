namespace InvestAgent.Core.Models;

/// <summary>
/// 工作流运行记录。
/// 记录某个子 Agent（如 Agent B/C/D）在一次触发中的完整执行步骤序列。
/// </summary>
public class WorkflowRunRecord
{
    /// <summary>记录唯一 ID（数据库自增）</summary>
    public long Id { get; set; }

    /// <summary>执行该工作流的 Agent 名称（Agent A / Agent B 等）</summary>
    public string AgentName { get; set; } = "";

    /// <summary>触发该工作流的对话轮次索引</summary>
    public int TriggerTurnIndex { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>该工作流执行的所有步骤</summary>
    public List<AgentStep> Steps { get; set; } = new();
}
