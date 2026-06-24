using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

/// <summary>
/// 会话分析编排器接口。
/// 负责初始全量分析的调度（并行执行 Agent B/C/D）以及
/// 会话内追问的意图解析和任务分发。
/// </summary>
public interface ISessionAnalysisOrchestrator
{
    /// <summary>
    /// 执行初始全量分析——并行调用 Agent B/C/D 完成 K线/新闻/财务全方位分析。
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="targetInput">用户输入的股票代码或名称</param>
    /// <param name="observer">可选的流式观察者</param>
    /// <returns>所有执行步骤的列表</returns>
    Task<List<AgentStep>> RunInitialAnalysisAsync(AgentSessionContext context, string targetInput, IAnalysisStreamingObserver? observer = null);

    /// <summary>
    /// 处理会话内的追问——解析用户意图，决定委托哪些子 Agent 执行增量分析。
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="userMessage">用户的追问文本</param>
    /// <param name="observer">可选的流式观察者</param>
    /// <returns>所有执行步骤的列表</returns>
    Task<List<AgentStep>> HandleChatAsync(AgentSessionContext context, string userMessage, IAnalysisStreamingObserver? observer = null);
}
