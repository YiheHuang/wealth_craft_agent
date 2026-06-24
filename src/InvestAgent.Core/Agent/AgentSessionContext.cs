using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent 会话上下文——整个分析会话的核心运行时容器。
/// 封装了对话记忆、会话状态（数据+分析结果）、消息列表和工作流运行记录。
/// 贯穿整个分析生命周期，在 Agent A/B/C/D 和 Orchestrator 之间共享。
/// </summary>
public class AgentSessionContext
{
    /// <summary>状态写锁——确保多线程写入 <see cref="State"/> 时线程安全</summary>
    private readonly object _stateLock = new();

    /// <summary>对话记忆（聊天历史管理）</summary>
    public ConversationMemory Memory { get; }

    /// <summary>会话完整状态快照</summary>
    public AnalysisSessionState State { get; }

    /// <summary>会话中的所有聊天消息</summary>
    public List<SessionChatMessage> Messages { get; }

    /// <summary>会话中的所有工作流运行记录</summary>
    public List<WorkflowRunRecord> WorkflowRuns { get; }

    public AgentSessionContext(
        ConversationMemory memory,
        AnalysisSessionState state,
        List<SessionChatMessage>? messages = null,
        List<WorkflowRunRecord>? workflowRuns = null)
    {
        Memory = memory;
        State = state;
        Messages = messages ?? new List<SessionChatMessage>();
        WorkflowRuns = workflowRuns ?? new List<WorkflowRunRecord>();
    }

    /// <summary>
    /// 应用状态补丁——以线程安全方式将增量更新合并到 <see cref="State"/>。
    /// </summary>
    /// <param name="patch">包含待更新字段的补丁（null 字段不覆盖）</param>
    public void ApplyPatch(SessionStatePatch patch)
    {
        lock (_stateLock)
            patch.ApplyTo(State);
    }
}
