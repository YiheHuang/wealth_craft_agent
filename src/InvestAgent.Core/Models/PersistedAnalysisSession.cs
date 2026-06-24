namespace InvestAgent.Core.Models;

/// <summary>
/// 持久化分析会话的完整数据聚合体。
/// 包含会话元数据、完整状态快照、所有消息和工作流运行记录。
/// 用于保存和恢复整个分析会话。
/// </summary>
public class PersistedAnalysisSession
{
    /// <summary>会话摘要记录（元数据）</summary>
    public AnalysisSessionRecord Record { get; set; } = new();

    /// <summary>会话完整状态快照（K线、新闻、财务、分析结果等）</summary>
    public AnalysisSessionState State { get; set; } = new();

    /// <summary>会话中所有的聊天消息</summary>
    public List<SessionChatMessage> Messages { get; set; } = new();

    /// <summary>会话中所有的工作流运行记录</summary>
    public List<WorkflowRunRecord> WorkflowRuns { get; set; } = new();
}
