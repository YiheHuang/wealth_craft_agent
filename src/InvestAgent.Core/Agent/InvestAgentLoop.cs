using InvestAgent.Core.Configuration;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace InvestAgent.Core.Agent;

public class InvestAgentLoop
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly IConversationMemory _memory;
    private readonly AgentOptions _options;
    private readonly ILogger<InvestAgentLoop> _logger;

    public event Action<AgentStep>? OnStep;

    public InvestAgentLoop(Kernel kernel, IChatCompletionService chatService,
        IConversationMemory memory, AgentOptions options, ILogger<InvestAgentLoop> logger)
    {
        _kernel = kernel;
        _chatService = chatService;
        _memory = memory;
        _options = options;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentStep> RunAsync(string userMessage)
    {
        _logger.LogInformation("AgentLoop 开始处理: {Message}", userMessage[..Math.Min(100, userMessage.Length)]);
        _memory.AddUserMessage(userMessage);

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.7,
            MaxTokens = 4096
        };

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            // [Thought] — 调用 LLM
            var thoughtStep = new AgentStep
            {
                StepNumber = step,
                Type = AgentStepType.Thought,
                Content = step == 1 ? "分析用户问题，判断需要调用哪些工具..." : "根据已有数据，判断是否需要更多信息..."
            };
            OnStep?.Invoke(thoughtStep);
            yield return thoughtStep;

            // 调用 LLM（可能抛异常）
            ChatMessageContent? response = null;
            string? errorMessage = null;
            try
            {
                response = await _chatService.GetChatMessageContentAsync(
                    _memory.GetChatHistory(), settings, _kernel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM 调用失败 (Step {Step})", step);
                errorMessage = $"抱歉，分析过程中出现错误：{ex.Message}";
            }

            if (errorMessage != null)
            {
                var errorStep = new AgentStep
                {
                    StepNumber = step,
                    Type = AgentStepType.Response,
                    Content = errorMessage
                };
                OnStep?.Invoke(errorStep);
                yield return errorStep;
                yield break;
            }

            // 检查是否有工具调用
            var functionCalls = response!.Items.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Count == 0)
            {
                var responseStep = new AgentStep
                {
                    StepNumber = step,
                    Type = AgentStepType.Response,
                    Content = response.Content ?? ""
                };
                _memory.AddAssistantMessage(response.Content ?? "");
                OnStep?.Invoke(responseStep);
                yield return responseStep;
                _logger.LogInformation("AgentLoop 完成, 共 {Steps} 步", step);
                yield break;
            }

            // [Action] + [Observation] — 执行函数调用
            _memory.AddAssistantMessage("");

            foreach (var functionCall in functionCalls)
            {
                var actionStep = new AgentStep
                {
                    StepNumber = step,
                    Type = AgentStepType.Action,
                    FunctionName = functionCall.FunctionName,
                    FunctionArgs = functionCall.Arguments?.ToString() ?? "{}",
                    Content = $"调用工具: {functionCall.FunctionName}"
                };
                OnStep?.Invoke(actionStep);
                yield return actionStep;

                string resultStr;
                try
                {
                    var result = await functionCall.InvokeAsync(_kernel);
                    resultStr = result?.ToString() ?? "工具执行完成（无返回数据）";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "工具 {Function} 执行失败", functionCall.FunctionName);
                    resultStr = $"执行失败: {ex.Message}";
                }

                var obsStep = new AgentStep
                {
                    StepNumber = step,
                    Type = AgentStepType.Observation,
                    FunctionName = functionCall.FunctionName,
                    FunctionResult = resultStr.Length > 2000 ? resultStr[..2000] + "..." : resultStr,
                    Content = $"工具 {functionCall.FunctionName} 返回结果"
                };
                OnStep?.Invoke(obsStep);
                yield return obsStep;

                _memory.AddToolMessage(resultStr, functionCall.FunctionName);
            }
        }

        _logger.LogWarning("达到最大步数 {MaxSteps}, 强制生成总结", _options.MaxSteps);
        var finalStep = new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "分析步数已达上限，请尝试提出更具体的问题以获取更精准的分析。"
        };
        OnStep?.Invoke(finalStep);
        yield return finalStep;
    }
}
