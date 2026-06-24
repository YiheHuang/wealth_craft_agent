using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace InvestAgent.Core.Memory;

/// <summary>
/// 对话记忆的默认实现。
/// 管理 AI 对话的完整生命周期——系统提示注入、消息流转、自动截断和摘要生成。
/// 每次添加用户消息时自动检查是否需要截断以控制上下文长度。
/// </summary>
public class ConversationMemory : IConversationMemory
{
    /// <summary>底层聊天历史存储</summary>
    private readonly ChatHistory _chatHistory;

    private readonly ILogger<ConversationMemory> _logger;

    /// <summary>最大保留轮次数，超过后自动截断</summary>
    private readonly int _maxTurns;

    /// <summary>当前对话轮次计数</summary>
    private int _turnCount;

    /// <inheritdoc />
    public int TurnCount => _turnCount;

    /// <summary>
    /// 初始化对话记忆。
    /// </summary>
    /// <param name="systemPrompt">系统提示词（注入为 System 消息）</param>
    /// <param name="maxTurns">最大保留轮次数</param>
    /// <param name="logger">日志记录器</param>
    public ConversationMemory(string systemPrompt, int maxTurns, ILogger<ConversationMemory> logger)
    {
        _logger = logger;
        _maxTurns = maxTurns;
        _chatHistory = new ChatHistory();
        _chatHistory.AddSystemMessage(systemPrompt);
    }

    /// <inheritdoc />
    public void AddUserMessage(string message)
    {
        _chatHistory.AddUserMessage(message);
        _turnCount++;
        TrimIfNeeded(); // 自动检查是否需要截断
    }

    /// <inheritdoc />
    public void AddAssistantMessage(string message)
    {
        _chatHistory.AddAssistantMessage(message);
    }

    /// <inheritdoc />
    public void AddToolMessage(string content, string functionName)
    {
        _chatHistory.Add(new ChatMessageContent(
            AuthorRole.Tool,
            content,
            metadata: new Dictionary<string, object?> { ["functionName"] = functionName }));
    }

    /// <inheritdoc />
    public ChatHistory GetChatHistory() => _chatHistory;

    /// <inheritdoc />
    public IReadOnlyList<ChatMessageContent> GetRecentMessages(int count = 10)
    {
        var messages = _chatHistory.ToList();
        var skip = Math.Max(0, messages.Count - count);
        return messages.Skip(skip).ToList();
    }

    /// <inheritdoc />
    public async Task<string> GetSummaryAsync()
    {
        // 简单摘要：拼接最近 20 条消息的前 200 字符
        var recentMessages = GetRecentMessages(20);
        var summary = string.Join("\n", recentMessages.Select(m => $"[{m.Role}]: {m.Content}"[..Math.Min(m.Content?.Length ?? 0, 200)]));
        return await Task.FromResult(summary);
    }

    /// <inheritdoc />
    public void Trim(int maxTurns)
    {
        var messages = _chatHistory.ToList();
        // 保留系统消息
        var systemMsg = messages.FirstOrDefault(m => m.Role == AuthorRole.System);
        var otherMsgs = messages.Where(m => m.Role != AuthorRole.System).ToList();

        // 每轮 = 用户消息 + 助手回复 = 约 2 条消息
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

    /// <summary>当轮次超过最大限制时自动触发截断</summary>
    private void TrimIfNeeded()
    {
        if (_turnCount > _maxTurns)
            Trim(_maxTurns);
    }
}
