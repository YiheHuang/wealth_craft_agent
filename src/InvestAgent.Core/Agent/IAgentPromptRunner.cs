using InvestAgent.Core.Memory;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent Prompt 运行器接口。
/// 封装了与 LLM 交互的完整流程——包括聊天历史构建、流式输出、图片输入支持。
/// 支持带/不带流式回调的两种调用模式，以及可选的多模态图片输入。
/// </summary>
public interface IAgentPromptRunner
{
    /// <summary>运行一次性 Prompt（非流式），返回完整响应</summary>
    Task<string> RunPromptAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12);

    /// <summary>运行流式 Prompt，通过回调逐片推送响应内容</summary>
    Task<string> RunPromptStreamingAsync(
        string systemPrompt,
        string userPrompt,
        Func<string, Task>? onPartial = null,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12);

    /// <summary>运行带图片的流式 Prompt（多模态），图片以 base64 方式注入消息</summary>
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
