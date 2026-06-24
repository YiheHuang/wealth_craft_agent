using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace InvestAgent.Core.Memory;

/// <summary>
/// 对话记忆接口。
/// 管理 LLM 对话的聊天历史记录，支持消息添加、摘要生成和自动截断。
/// 底层基于 Semantic Kernel 的 <see cref="ChatHistory"/>。
/// </summary>
public interface IConversationMemory
{
    /// <summary>添加用户消息，并递增轮次计数</summary>
    void AddUserMessage(string message);

    /// <summary>添加助手（AI）回复消息</summary>
    void AddAssistantMessage(string message);

    /// <summary>添加工具调用返回的消息</summary>
    /// <param name="content">工具返回的内容</param>
    /// <param name="functionName">工具/函数名称</param>
    void AddToolMessage(string content, string functionName);

    /// <summary>获取完整的聊天历史（用于传给 LLM）</summary>
    ChatHistory GetChatHistory();

    /// <summary>获取最近的 N 条消息</summary>
    /// <param name="count">消息数量，默认 10</param>
    IReadOnlyList<ChatMessageContent> GetRecentMessages(int count = 10);

    /// <summary>当前对话轮次计数</summary>
    int TurnCount { get; }

    /// <summary>生成当前对话的文本摘要（用于状态注入）</summary>
    Task<string> GetSummaryAsync();

    /// <summary>
    /// 截断对话历史，仅保留最近的指定轮次。
    /// 系统消息（System Prompt）会被保留。
    /// </summary>
    /// <param name="maxTurns">最大保留轮次数</param>
    void Trim(int maxTurns);
}
