using InvestAgent.Core.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace InvestAgent.Core.Agent;

public class AgentPromptRunner : IAgentPromptRunner
{
    private const int MinStreamingNotificationIntervalMs = 80;
    private const int MinStreamingNotificationChars = 96;
    private const int MaxStreamingAttempts = 3;
    private const string ReasoningTagPattern = "think|thinking|analysis|reasoning|thought|thing";

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
        return await RunPromptStreamingAsync(
            systemPrompt,
            userPrompt,
            null,
            temperature,
            memory,
            stateSummary,
            recentMessageCount);
    }

    public async Task<string> RunPromptStreamingAsync(
        string systemPrompt,
        string userPrompt,
        Func<string, Task>? onPartial = null,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12)
    {
        var history = await BuildHistoryAsync(systemPrompt, userPrompt, memory, stateSummary, recentMessageCount, null);
        return await RunStreamingAsync(history, onPartial, temperature);
    }

    public async Task<string> RunPromptStreamingWithImagesAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<PromptImageInput> images,
        Func<string, Task>? onPartial = null,
        double temperature = 0.3,
        IConversationMemory? memory = null,
        string? stateSummary = null,
        int recentMessageCount = 12)
    {
        if (images.Count == 0)
            return await RunPromptStreamingAsync(systemPrompt, userPrompt, onPartial, temperature, memory, stateSummary, recentMessageCount);

        try
        {
            var history = await BuildHistoryAsync(systemPrompt, userPrompt, memory, stateSummary, recentMessageCount, images);
            return await RunStreamingAsync(history, onPartial, temperature);
        }
        catch
        {
            var fallbackPrompt = userPrompt +
                                 "\n\n注意：本轮检索到了缠论图例，但当前模型或接口未能接收图片输入。请只基于上文的图片元数据、来源页面和本地路径进行引用，不要声称已经视觉识别图片细节。";
            return await RunPromptStreamingAsync(systemPrompt, fallbackPrompt, onPartial, temperature, memory, stateSummary, recentMessageCount);
        }
    }

    private async Task<string> RunStreamingAsync(
        ChatHistory history,
        Func<string, Task>? onPartial,
        double temperature)
    {

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = temperature,
            MaxTokens = 4096
        };

        for (var attempt = 1; attempt <= MaxStreamingAttempts; attempt++)
        {
            var builder = new StringBuilder();
            var notifyTimer = Stopwatch.StartNew();
            var lastNotifiedLength = 0;

            try
            {
                await foreach (var chunk in _chatCompletionService.GetStreamingChatMessageContentsAsync(history, settings))
                {
                    if (string.IsNullOrWhiteSpace(chunk.Content))
                        continue;

                    builder.Append(chunk.Content);

                    if (onPartial is not null &&
                        (builder.Length - lastNotifiedLength >= MinStreamingNotificationChars ||
                         notifyTimer.ElapsedMilliseconds >= MinStreamingNotificationIntervalMs))
                    {
                        await onPartial(SanitizeAssistantOutput(builder.ToString(), allowPartial: true));
                        lastNotifiedLength = builder.Length;
                        notifyTimer.Restart();
                    }
                }

                if (onPartial is not null && builder.Length != lastNotifiedLength)
                    await onPartial(SanitizeAssistantOutput(builder.ToString(), allowPartial: true));

                var final = SanitizeAssistantOutput(builder.ToString(), allowPartial: false);
                if (onPartial is not null)
                    await onPartial(final);

                return final;
            }
            catch (Exception ex) when (IsTransientNetworkError(ex) && attempt < MaxStreamingAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600 * attempt));
            }
        }

        return "";
    }

    private static bool IsTransientNetworkError(Exception ex)
    {
        return ex is HttpRequestException or IOException or TaskCanceledException ||
               ex.InnerException is not null && IsTransientNetworkError(ex.InnerException);
    }

    private static async Task<ChatHistory> BuildHistoryAsync(
        string systemPrompt,
        string userPrompt,
        IConversationMemory? memory,
        string? stateSummary,
        int recentMessageCount,
        IReadOnlyList<PromptImageInput>? images)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt + "\n\n输出规则：只输出给用户看的最终答案，不要输出 <think>、</think>、<thing>、推理过程、思考草稿、分析步骤或内部计划。");

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
                    history.AddAssistantMessage(SanitizeAssistantOutput(message.Content ?? "", allowPartial: false));
                else if (message.Role == AuthorRole.Tool)
                    history.Add(new ChatMessageContent(AuthorRole.Tool, message.Content ?? "", metadata: message.Metadata));
            }
        }

        if (images is { Count: > 0 })
        {
            var items = new ChatMessageContentItemCollection
            {
                new TextContent(userPrompt)
            };

            foreach (var image in images)
            {
                if (string.IsNullOrWhiteSpace(image.LocalPath) || !File.Exists(image.LocalPath))
                    continue;

                var bytes = await File.ReadAllBytesAsync(image.LocalPath);
                var mimeType = string.IsNullOrWhiteSpace(image.MimeType) ? GetMimeType(image.LocalPath) : image.MimeType;
                items.Add(new ImageContent(bytes, mimeType));
            }

            if (items.Count > 1)
                history.AddUserMessage(items);
            else
                history.AddUserMessage(userPrompt);
        }
        else
        {
            history.AddUserMessage(userPrompt);
        }

        return history;
    }

    private static string GetMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private static string SanitizeAssistantOutput(string content, bool allowPartial)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var text = content.Replace("\r\n", "\n").Replace('\r', '\n');

        text = Regex.Replace(text, $@"(?is)<\s*(?:{ReasoningTagPattern})\b[^>]*>.*?<\s*/\s*(?:{ReasoningTagPattern})\s*>", "");

        if (allowPartial)
            text = Regex.Replace(text, $@"(?is)<\s*(?:{ReasoningTagPattern})\b[^>]*>.*$", "");

        text = RemoveDanglingReasoningCloseTag(text);
        text = RemoveLeadingReasoningSection(text, allowPartial);
        text = Regex.Replace(text, @"(?im)^\s*(?:最终答案|正式回答|回答|正文)\s*[:：]\s*", "");
        text = Regex.Replace(text, $@"(?im)^\s*</?\s*(?:{ReasoningTagPattern})\s*>\s*$", "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string RemoveDanglingReasoningCloseTag(string text)
    {
        var matches = Regex.Matches(text, $@"(?is)<\s*/\s*(?:{ReasoningTagPattern})\s*>");
        if (matches.Count == 0)
            return text;

        var last = matches[matches.Count - 1];
        var prefix = text[..last.Index];
        var suffix = text[(last.Index + last.Length)..];

        if (LooksLikeReasoning(prefix) || Regex.IsMatch(prefix, @"(?im)^\s*\d+\.\s+"))
            return suffix.TrimStart();

        return Regex.Replace(text, $@"(?is)<\s*/\s*(?:{ReasoningTagPattern})\s*>", "");
    }

    private static string RemoveLeadingReasoningSection(string text, bool allowPartial)
    {
        var marker = Regex.Match(
            text,
            @"(?im)^\s*(?:#{1,6}\s*)?(?:最终答案|正式回答|回答|正文|结论|公司主要业务|K线分析|新闻分析|财务分析|风险提示|投资建议|缠论视角|缠论分析)\s*[:：]?");

        if (marker.Success && marker.Index > 0)
        {
            var prefix = text[..marker.Index];
            if (LooksLikeReasoning(prefix))
                return text[marker.Index..];
        }

        if (Regex.IsMatch(text, @"(?im)^\s*(?:#{1,6}\s*)?(?:思考过程|我的思考|推理过程|内部推理|Chain\s*of\s*Thought|Thought\s*Process|Reasoning)\s*[:：]"))
        {
            if (allowPartial && !marker.Success)
                return "";

            if (marker.Success)
                return text[marker.Index..];
        }

        return text;
    }

    private static bool LooksLikeReasoning(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return Regex.IsMatch(
            text,
            @"(?im)(思考过程|我的思考|推理过程|内部推理|先分析|我需要|用户要求|需要结合|当前数据|分析步骤|组织语言|直接回答用户问题|Chain\s*of\s*Thought|Thought\s*Process|Reasoning|We need|I need)");
    }
}
