namespace InvestAgent.Core.Models;

/// <summary>
/// Agent 执行步骤的类型枚举。
/// 遵循 ReAct（Reasoning + Acting）模式的 Thought → Action → Observation → Response 循环。
/// </summary>
public enum AgentStepType
{
    /// <summary>思考阶段：Agent 分析当前状态并决定下一步行动</summary>
    Thought,

    /// <summary>行动阶段：Agent 调用工具/函数</summary>
    Action,

    /// <summary>观察阶段：接收工具/函数返回的结果</summary>
    Observation,

    /// <summary>最终响应阶段：向用户输出答案</summary>
    Response
}

/// <summary>
/// Agent 执行过程中的单步记录。
/// 可序列化，用于步骤追踪、UI 展示和会话回放。
/// </summary>
public class AgentStep
{
    /// <summary>步骤序号（从 1 开始）</summary>
    public int StepNumber { get; set; }

    /// <summary>步骤类型</summary>
    public AgentStepType Type { get; set; }

    /// <summary>步骤的文字描述内容</summary>
    public string Content { get; set; } = "";

    /// <summary>调用的函数/工具名称（Action/Observation 步骤时使用）</summary>
    public string? FunctionName { get; set; }

    /// <summary>传递给函数的参数（JSON 字符串）</summary>
    public string? FunctionArgs { get; set; }

    /// <summary>函数返回的结果（可能截断）</summary>
    public string? FunctionResult { get; set; }

    /// <summary>步骤发生时间</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
