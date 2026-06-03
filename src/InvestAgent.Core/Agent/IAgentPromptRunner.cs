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
}
