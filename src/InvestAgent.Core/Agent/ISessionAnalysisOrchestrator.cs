using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

public interface ISessionAnalysisOrchestrator
{
    Task<List<AgentStep>> RunInitialAnalysisAsync(AgentSessionContext context, string targetInput);
    Task<List<AgentStep>> HandleChatAsync(AgentSessionContext context, string userMessage);
}
