using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

public class AgentSessionContext
{
    private readonly object _stateLock = new();

    public ConversationMemory Memory { get; }
    public AnalysisSessionState State { get; }
    public List<SessionChatMessage> Messages { get; }
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

    public void ApplyPatch(SessionStatePatch patch)
    {
        lock (_stateLock)
            patch.ApplyTo(State);
    }
}
