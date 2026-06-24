using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

/// <summary>
/// 子 Agent 服务接口。
/// Agent B（K线/技术）、Agent C（新闻/情绪）、Agent D（财务）
/// 均实现此接口，由 <see cref="ISessionAnalysisOrchestrator"/> 统一调度。
/// </summary>
public interface ISubAgentService
{
    /// <summary>Agent 名称标识（"Agent B" / "Agent C" / "Agent D"）</summary>
    string AgentName { get; }

    /// <summary>
    /// 执行子 Agent 的分析任务。
    /// </summary>
    /// <param name="context">当前会话上下文</param>
    /// <param name="task">任务定义（包含指令、参数、是否使用缠论等）</param>
    /// <param name="observer">可选的流式观察者</param>
    /// <param name="triggerTurnIndex">触发此任务的对话轮次</param>
    /// <returns>包含分析结果、状态补丁和步骤记录的执行结果</returns>
    Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task, IAnalysisStreamingObserver? observer = null, int triggerTurnIndex = 0);
}
