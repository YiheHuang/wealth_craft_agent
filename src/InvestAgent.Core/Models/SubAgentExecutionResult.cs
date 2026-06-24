namespace InvestAgent.Core.Models;

/// <summary>
/// 子 Agent 执行结果。
/// 包含 Agent 生成的分析文本、状态补丁以及工作流步骤记录。
/// </summary>
public class SubAgentExecutionResult
{
    /// <summary>执行该任务的 Agent 名称</summary>
    public string AgentName { get; set; } = "";

    /// <summary>Agent 输出的分析叙述（Markdown）</summary>
    public string NarrativeResult { get; set; } = "";

    /// <summary>
    /// 本次执行对会话状态的变更补丁。
    /// 包含新增数据（K线、新闻、财务等）和更新的分析结果字段。
    /// </summary>
    public SessionStatePatch StatePatch { get; set; } = new();

    /// <summary>执行过程中产生的所有步骤记录</summary>
    public List<AgentStep> WorkflowSteps { get; set; } = new();
}
