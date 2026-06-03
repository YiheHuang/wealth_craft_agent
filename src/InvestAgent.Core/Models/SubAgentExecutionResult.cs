namespace InvestAgent.Core.Models;

public class SubAgentExecutionResult
{
    public string AgentName { get; set; } = "";
    public string NarrativeResult { get; set; } = "";
    public SessionStatePatch StatePatch { get; set; } = new();
    public List<AgentStep> WorkflowSteps { get; set; } = new();
}
