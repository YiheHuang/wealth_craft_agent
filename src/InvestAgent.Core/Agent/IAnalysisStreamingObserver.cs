using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

/// <summary>
/// 分析过程流式观察者接口。
/// 允许 UI 层（桌面/控制台）实时接收 Agent 分析过程中的每一步变化。
/// 实现观察者模式，解耦分析引擎和显示层。
/// </summary>
public interface IAnalysisStreamingObserver
{
    /// <summary>子 Agent 产生新步骤时回调</summary>
    /// <param name="context">当前会话上下文</param>
    /// <param name="agentName">产生步骤的 Agent 名称</param>
    /// <param name="step">步骤详情</param>
    /// <param name="turnIndex">当前轮次</param>
    Task OnStepAddedAsync(AgentSessionContext context, string agentName, AgentStep step, int turnIndex);

    /// <summary>会话状态被部分更新时回调</summary>
    Task OnStatePatchedAsync(AgentSessionContext context, SessionStatePatch patch);

    /// <summary>新聊天消息被添加时回调</summary>
    Task OnMessageAddedAsync(AgentSessionContext context, SessionChatMessage message);

    /// <summary>聊天消息内容被更新时回调（流式输出场景）</summary>
    Task OnMessageUpdatedAsync(AgentSessionContext context, SessionChatMessage message);
}
