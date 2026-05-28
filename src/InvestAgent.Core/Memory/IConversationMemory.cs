using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace InvestAgent.Core.Memory;

public interface IConversationMemory
{
    void AddUserMessage(string message);
    void AddAssistantMessage(string message);
    void AddToolMessage(string content, string functionName);
    ChatHistory GetChatHistory();
    IReadOnlyList<ChatMessageContent> GetRecentMessages(int count = 10);
    int TurnCount { get; }
    Task<string> GetSummaryAsync();
    void Trim(int maxTurns);
}
