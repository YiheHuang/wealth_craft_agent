namespace InvestAgent.Core.Models;

/// <summary>
/// 会话聊天消息模型。
/// 记录分析会话中的每一轮对话（用户提问 / Agent 回复），可持久化到 SQLite。
/// </summary>
public class SessionChatMessage
{
    /// <summary>消息唯一 ID（数据库自增）</summary>
    public long Id { get; set; }

    /// <summary>消息角色：user（用户）或 assistant（助手）</summary>
    public string Role { get; set; } = "";

    /// <summary>消息正文内容</summary>
    public string Content { get; set; } = "";

    /// <summary>消息创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>所属对话轮次索引（从 1 开始，同一轮次可有多条消息）</summary>
    public int TurnIndex { get; set; }
}
