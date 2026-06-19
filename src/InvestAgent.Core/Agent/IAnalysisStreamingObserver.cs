using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

public interface IAnalysisStreamingObserver
{
    Task OnStepAddedAsync(AgentSessionContext context, string agentName, AgentStep step, int turnIndex);
    Task OnStatePatchedAsync(AgentSessionContext context, SessionStatePatch patch);
    Task OnMessageAddedAsync(AgentSessionContext context, SessionChatMessage message);
    Task OnMessageUpdatedAsync(AgentSessionContext context, SessionChatMessage message);
}
