using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

public interface ISubAgentService
{
    string AgentName { get; }
    Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task);
}
