using InvestAgent.Core.Configuration;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Agent;

public class AgentSessionFactory : IAgentSessionFactory
{
    private readonly AgentOptions _options;
    private readonly ILogger<ConversationMemory> _memoryLogger;
    private readonly ISystemPromptProvider _systemPromptProvider;

    public AgentSessionFactory(
        AgentOptions options,
        ILogger<ConversationMemory> memoryLogger,
        ISystemPromptProvider systemPromptProvider)
    {
        _options = options;
        _memoryLogger = memoryLogger;
        _systemPromptProvider = systemPromptProvider;
    }

    public AgentSessionContext Create(string symbol, string stockName = "", long sessionId = 0)
    {
        var state = new AnalysisSessionState
        {
            SessionId = sessionId,
            Symbol = symbol,
            StockName = stockName,
            SessionTitle = string.IsNullOrWhiteSpace(stockName) ? symbol : $"{symbol} {stockName}",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        return new AgentSessionContext(CreateMemory(), state);
    }

    public AgentSessionContext Restore(PersistedAnalysisSession persistedSession)
    {
        var memory = CreateMemory();
        foreach (var message in persistedSession.Messages.OrderBy(x => x.TurnIndex).ThenBy(x => x.CreatedAt))
        {
            switch (message.Role)
            {
                case "user":
                    memory.AddUserMessage(message.Content);
                    break;
                case "assistant":
                    memory.AddAssistantMessage(message.Content);
                    break;
            }
        }

        return new AgentSessionContext(
            memory,
            persistedSession.State,
            persistedSession.Messages,
            persistedSession.WorkflowRuns);
    }

    private ConversationMemory CreateMemory()
    {
        return new ConversationMemory(
            _systemPromptProvider.GetDefaultSystemPrompt(),
            _options.MaxConversationTurns,
            _memoryLogger);
    }
}
