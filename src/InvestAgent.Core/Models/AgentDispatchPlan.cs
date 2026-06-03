namespace InvestAgent.Core.Models;

public class AgentDispatchPlan
{
    public string Mode { get; set; } = "delegate";
    public string UserFacingMessage { get; set; } = "";
    public List<SubAgentTask> Tasks { get; set; } = new();
}
