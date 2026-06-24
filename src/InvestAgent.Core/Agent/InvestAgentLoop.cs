using InvestAgent.Core.Configuration;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace InvestAgent.Core.Agent;

/// <summary>
/// 投资分析 Agent 的主执行循环。
/// 实现 ReAct（Reasoning + Acting）模式：Thought → Action → Observation → Response。
/// 在循环中反复调用 LLM，执行其请求的工具调用，直到 LLM 给出最终响应或达到最大步数。
/// 通过 <see cref="OnStep"/> 事件向 UI 层推送每一步的执行状态。
/// </summary>
public class InvestAgentLoop
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly IConversationMemory _memory;
    private readonly AgentOptions _options;
    private readonly ILogger<InvestAgentLoop> _logger;

    /// <summary>步骤执行事件——每完成一步（Thought/Action/Observation/Response）触发</summary>
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

    /// <summary>
    /// 执行 Agent 主循环。
    /// 这是一个异步迭代器——每一步都通过 yield return 推送给调用方，
    /// 同时触发 <see cref="OnStep"/> 事件。
    /// </summary>
    /// <param name="userMessage">用户输入的自然语言问题</param>
    /// <returns>每一步的 AgentStep 流</returns>
    public async IAsyncEnumerable<AgentStep> RunAsync(string userMessage)
    {
        _logger.LogInformation("AgentLoop 开始处理: {Message}", userMessage[..Math.Min(100, userMessage.Length)]);
        _memory.AddUserMessage(userMessage);

        // LLM 调用参数：自动选择工具、温度 0.7、最大 4096 tokens
        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.7,
            MaxTokens = 4096
        };

        // 主循环——最多执行 MaxSteps 步
        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            // ── [Thought] 思考阶段 ──────────────────────────
            var thoughtStep = new AgentStep
            {
                StepNumber = step,
                Type = AgentStepType.Thought,
                Content = step == 1
                    ? "分析用户问题，判断需要调用哪些工具..."
                    : "根据已有数据，判断是否需要更多信息..."
            };
            OnStep?.Invoke(thoughtStep);
            yield return thoughtStep;

            // ── 调用 LLM ─────────────────────────────────────
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

            // LLM 调用异常 → 输出错误并终止
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

            // ── 检查是否有工具调用 ──────────────────────────
            var functionCalls = response!.Items.OfType<FunctionCallContent>().ToList();

            // 无工具调用 → LLM 给出了最终答案
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

            // ── [Action] + [Observation] 执行工具调用 ────────
            _memory.AddAssistantMessage("");

            foreach (var functionCall in functionCalls)
            {
                // Action：记录工具调用
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

                // 执行工具函数
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

                // Observation：记录工具返回
                var obsStep = new AgentStep
                {
                    StepNumber = step,
                    Type = AgentStepType.Observation,
                    FunctionName = functionCall.FunctionName,
                    // 结果截断——避免超长 JSON 撑爆上下文
                    FunctionResult = resultStr.Length > 2000 ? resultStr[..2000] + "..." : resultStr,
                    Content = $"工具 {functionCall.FunctionName} 返回结果"
                };
                OnStep?.Invoke(obsStep);
                yield return obsStep;

                _memory.AddToolMessage(resultStr, functionCall.FunctionName);
            }
        }

        // 达到最大步数 → 强制终止
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
