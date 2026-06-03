namespace InvestAgent.Core.Models;

public class WorkflowRunRecord
{
    public long Id { get; set; }
    public string AgentName { get; set; } = "";
    public int TriggerTurnIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<AgentStep> Steps { get; set; } = new();
}
