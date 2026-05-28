using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace InvestAgent.Core.Memory;

public class ConversationMemory : IConversationMemory
{
    private readonly ChatHistory _chatHistory;
    private readonly ILogger<ConversationMemory> _logger;
    private readonly int _maxTurns;
    private int _turnCount;

    public int TurnCount => _turnCount;

    public ConversationMemory(string systemPrompt, int maxTurns, ILogger<ConversationMemory> logger)
    {
        _logger = logger;
        _maxTurns = maxTurns;
        _chatHistory = new ChatHistory();
        _chatHistory.AddSystemMessage(systemPrompt);
    }

    public void AddUserMessage(string message)
    {
        _chatHistory.AddUserMessage(message);
        _turnCount++;
        TrimIfNeeded();
    }

    public void AddAssistantMessage(string message)
    {
        _chatHistory.AddAssistantMessage(message);
    }

    public void AddToolMessage(string content, string functionName)
    {
        _chatHistory.Add(new ChatMessageContent(
            AuthorRole.Tool,
            content,
            metadata: new Dictionary<string, object?> { ["functionName"] = functionName }));
    }

    public ChatHistory GetChatHistory() => _chatHistory;

    public IReadOnlyList<ChatMessageContent> GetRecentMessages(int count = 10)
    {
        var messages = _chatHistory.ToList();
        var skip = Math.Max(0, messages.Count - count);
        return messages.Skip(skip).ToList();
    }

    public async Task<string> GetSummaryAsync()
    {
        var recentMessages = GetRecentMessages(20);
        var summary = string.Join("\n", recentMessages.Select(m => $"[{m.Role}]: {m.Content}"[..Math.Min(m.Content?.Length ?? 0, 200)]));
        return await Task.FromResult(summary);
    }

    public void Trim(int maxTurns)
    {
        var messages = _chatHistory.ToList();
        var systemMsg = messages.FirstOrDefault(m => m.Role == AuthorRole.System);
        var otherMsgs = messages.Where(m => m.Role != AuthorRole.System).ToList();

        var turnsToKeep = maxTurns * 2;
        if (otherMsgs.Count > turnsToKeep)
        {
            _chatHistory.Clear();
            if (systemMsg != null)
                _chatHistory.Add(systemMsg);
            foreach (var msg in otherMsgs.Skip(otherMsgs.Count - turnsToKeep))
                _chatHistory.Add(msg);
            _logger.LogDebug("对话记忆截断: {Old} -> {New} 条消息", otherMsgs.Count, turnsToKeep);
        }
    }

    private void TrimIfNeeded()
    {
        if (_turnCount > _maxTurns)
            Trim(_maxTurns);
    }
}
