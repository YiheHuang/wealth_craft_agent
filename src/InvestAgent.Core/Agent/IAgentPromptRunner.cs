using InvestAgent.Core.Memory;

namespace InvestAgent.Core.Agent;

public interface IAgentPromptRunner
{
    Task<string> RunPromptAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12);

    Task<string> RunPromptStreamingAsync(
        string systemPrompt,
        string userPrompt,
        Func<string, Task>? onPartial = null,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12);

    Task<string> RunPromptStreamingWithImagesAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<PromptImageInput> images,
        Func<string, Task>? onPartial = null,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12);
}
