namespace InvestAgent.Core.Models;

public class PersistedAnalysisSession
{
    public AnalysisSessionRecord Record { get; set; } = new();
    public AnalysisSessionState State { get; set; } = new();
    public List<SessionChatMessage> Messages { get; set; } = new();
    public List<WorkflowRunRecord> WorkflowRuns { get; set; } = new();
}
