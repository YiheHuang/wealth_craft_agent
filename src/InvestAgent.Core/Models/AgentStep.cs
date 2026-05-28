namespace InvestAgent.Core.Models;

public enum AgentStepType
{
    Thought,
    Action,
    Observation,
    Response
}

public class AgentStep
{
    public int StepNumber { get; set; }
    public AgentStepType Type { get; set; }
    public string Content { get; set; } = "";
    public string? FunctionName { get; set; }
    public string? FunctionArgs { get; set; }
    public string? FunctionResult { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
