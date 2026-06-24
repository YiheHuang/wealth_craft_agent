using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent 会话工厂接口。
/// 负责创建新的分析会话上下文或从持久化数据恢复已有会话。
/// </summary>
public interface IAgentSessionFactory
{
    /// <summary>
    /// 创建新的分析会话。
    /// </summary>
    /// <param name="symbol">股票代码</param>
    /// <param name="stockName">股票名称（可选）</param>
    /// <param name="sessionId">会话 ID（0 表示新建）</param>
    /// <returns>初始化完成的会话上下文</returns>
    AgentSessionContext Create(string symbol, string stockName = "", long sessionId = 0);

    /// <summary>
    /// 从持久化数据恢复已有会话。
    /// 会重建对话记忆并回放所有历史消息。
    /// </summary>
    /// <param name="persistedSession">持久化的完整会话数据</param>
    /// <returns>恢复后的会话上下文</returns>
    AgentSessionContext Restore(PersistedAnalysisSession persistedSession);
}
