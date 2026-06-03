using InvestAgent.Core.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace InvestAgent.Core.Agent;

public class AgentPromptRunner : IAgentPromptRunner
{
    private readonly IChatCompletionService _chatCompletionService;

    public AgentPromptRunner(IChatCompletionService chatCompletionService)
    {
        _chatCompletionService = chatCompletionService;
    }

    public async Task<string> RunPromptAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12)
    {
        var history = await BuildHistoryAsync(systemPrompt, userPrompt, memory, stateSummary, recentMessageCount);

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = temperature,
            MaxTokens = 4096
        };

        var result = await _chatCompletionService.GetChatMessageContentAsync(history, settings);
        return result.Content ?? "";
    }

    private static async Task<ChatHistory> BuildHistoryAsync(
        string systemPrompt,
        string userPrompt,
        IConversationMemory? memory,
        string? stateSummary,
        int recentMessageCount)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        if (!string.IsNullOrWhiteSpace(stateSummary))
            history.AddSystemMessage($"当前会话状态摘要：\n{stateSummary}");

        if (memory is not null)
        {
            var summary = await memory.GetSummaryAsync();
            if (!string.IsNullOrWhiteSpace(summary))
                history.AddSystemMessage($"最近会话摘要：\n{summary}");

            foreach (var message in memory.GetRecentMessages(recentMessageCount))
            {
                if (message.Role == AuthorRole.System)
                    continue;

                if (message.Role == AuthorRole.User)
                    history.AddUserMessage(message.Content ?? "");
                else if (message.Role == AuthorRole.Assistant)
                    history.AddAssistantMessage(message.Content ?? "");
                else if (message.Role == AuthorRole.Tool)
                    history.Add(new ChatMessageContent(AuthorRole.Tool, message.Content ?? "", metadata: message.Metadata));
            }
        }

        history.AddUserMessage(userPrompt);
        return history;
    }
}
