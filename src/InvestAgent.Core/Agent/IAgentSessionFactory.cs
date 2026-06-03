using InvestAgent.Core.Models;

namespace InvestAgent.Core.Agent;

public interface IAgentSessionFactory
{
    AgentSessionContext Create(string symbol, string stockName = "", long sessionId = 0);
    AgentSessionContext Restore(PersistedAnalysisSession persistedSession);
}
