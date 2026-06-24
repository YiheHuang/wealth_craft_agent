using InvestAgent.Core.Configuration;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent 会话工厂的默认实现。
/// 创建新会话时注入系统提示词并初始化空状态；
/// 恢复会话时重建对话记忆并逐条回放历史消息以恢复上下文。
/// </summary>
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public AgentSessionContext Restore(PersistedAnalysisSession persistedSession)
    {
        var memory = CreateMemory();

        // 按轮次和时间顺序回放历史消息，重建对话记忆
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

    /// <summary>创建带有系统提示词的新对话记忆实例</summary>
    private ConversationMemory CreateMemory()
    {
        return new ConversationMemory(
            _systemPromptProvider.GetDefaultSystemPrompt(),
            _options.MaxConversationTurns,
            _memoryLogger);
    }
}
