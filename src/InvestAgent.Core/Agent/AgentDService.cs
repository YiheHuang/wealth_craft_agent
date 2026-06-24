using System.Text;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent D —— 财务分析服务。
/// 负责财务指标历史序列的抓取、趋势分析和 LLM 驱动的财务评估报告生成。
/// </summary>
public class AgentDService : ISubAgentService
{
    private readonly IStockDataService _stockDataService;
    private readonly IAgentPromptRunner _promptRunner;

    public string AgentName => "Agent D";

    public AgentDService(IStockDataService stockDataService, IAgentPromptRunner promptRunner)
    {
        _stockDataService = stockDataService;
        _promptRunner = promptRunner;
    }

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task, IAnalysisStreamingObserver? observer = null, int triggerTurnIndex = 0)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var isInitialAnalysis = task.IsInitialAnalysis;
        var financialYears = task.FinancialYears ?? context.State.FinancialYears;
        var reportCount = Math.Max(4, financialYears * 4);

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的财务请求展开分析，目标窗口 {financialYears} 年。"
        }, observer, triggerTurnIndex);

        var history = await _stockDataService.GetKeyMetricsHistoryAsync(context.State.Symbol, reportCount);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_key_metrics_history",
            FunctionArgs = $"{{\"symbol\":\"{context.State.Symbol}\",\"maxReports\":{reportCount}}}",
            Content = "抓取财务序列并评估趋势。"
        }, observer, triggerTurnIndex);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_key_metrics_history",
            FunctionResult = $"财务记录 {history.Count} 条",
            Content = "财务序列抓取完成。"
        }, observer, triggerTurnIndex);

        var dataPatch = new SessionStatePatch
        {
            FinancialYears = financialYears,
            FinancialHistory = history.OrderByDescending(x => x.ReportDate).ToList()
        };
        context.ApplyPatch(dataPatch);
        await NotifyStatePatchedAsync(context, dataPatch, observer);

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            Content = "Agent D 正在生成财务趋势分析。"
        }, observer, triggerTurnIndex);

        var narrative = await _promptRunner.RunPromptStreamingAsync(
            BuildSystemPrompt(isInitialAnalysis),
            BuildPrompt(context.State.Symbol, task.Instruction, financialYears, history, isInitialAnalysis),
            async partial =>
            {
                var patch = new SessionStatePatch
                {
                    AgentDResult = partial
                };
                context.ApplyPatch(patch);
                await NotifyStatePatchedAsync(context, patch, observer);
            },
            0.2,
            context.Memory,
            BuildStateSummary(context.State));

        result.NarrativeResult = narrative;
        result.StatePatch = new SessionStatePatch
        {
            FinancialYears = financialYears,
            FinancialHistory = history.OrderByDescending(x => x.ReportDate).ToList(),
            AgentDResult = narrative
        };
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent D 已完成财务趋势分析。"
        }, observer, triggerTurnIndex);
        return result;
    }

    private async Task AppendStepAsync(AgentSessionContext context, SubAgentExecutionResult result, AgentStep step, IAnalysisStreamingObserver? observer, int triggerTurnIndex)
    {
        result.WorkflowSteps.Add(step);
        if (observer is not null)
            await observer.OnStepAddedAsync(context, AgentName, step, triggerTurnIndex);
    }

    private static async Task NotifyStatePatchedAsync(AgentSessionContext context, SessionStatePatch patch, IAnalysisStreamingObserver? observer)
    {
        if (observer is not null)
            await observer.OnStatePatchedAsync(context, patch);
    }

    private static string BuildSystemPrompt(bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是 Agent D，负责财务分析。请围绕盈利能力、增长、负债、趋势一致性进行细致分析。");
        sb.AppendLine("不要自称 Agent D，不要问候，不要输出思考过程或内部推理，直接输出用户可读的 Markdown 分析。");
        if (isInitialAnalysis)
            sb.AppendLine("这是首次总览分析，请适度结构化，便于用户建立全局财务认知。");
        else
            sb.AppendLine("这是会话内追问，请优先回答用户当前最关心的财务问题，允许更自由地展开比较、解释和判断。");
        return sb.ToString();
    }

    private static string BuildPrompt(string symbol, string instruction, int financialYears, List<KeyMetrics> history, bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"标的: {symbol}");
        sb.AppendLine($"用户要求: {instruction}");
        sb.AppendLine($"财务窗口: 最近 {financialYears} 年");
        sb.AppendLine();
        foreach (var item in history.OrderByDescending(x => x.ReportDate))
        {
            sb.AppendLine($"{item.ReportDate:yyyy-MM-dd} | ROE={item.ROE:F2}% | ROA={item.ROA:F2}% | 毛利率={item.GrossMargin:F2}% | 净利率={item.NetMargin:F2}% | 营收增长={item.RevenueGrowth:F2}% | 净利增长={item.ProfitGrowth:F2}% | 负债率={item.DebtRatio:F2}%");
        }
        sb.AppendLine();
        if (isInitialAnalysis)
            sb.AppendLine("请输出较结构化的首次总览分析，建议覆盖：盈利能力、成长性、负债结构、趋势一致性、潜在风险。");
        else
            sb.AppendLine("请围绕用户当前追问自由分析。可以重点比较不同年份、解释指标变化原因、指出关键拐点，不必机械套固定模板。");
        return sb.ToString();
    }

    private static string BuildStateSummary(AnalysisSessionState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"当前股票: {state.Symbol} {state.StockName}");
        sb.AppendLine($"当前财务窗口: {state.FinancialYears} 年");
        sb.AppendLine($"当前财务记录数: {state.FinancialHistory.Count}");
        if (state.FinancialHistory.Count > 0)
        {
            var latest = state.FinancialHistory.OrderByDescending(x => x.ReportDate).First();
            sb.AppendLine($"最近报告期: {latest.ReportDate:yyyy-MM-dd}");
        }
        if (!string.IsNullOrWhiteSpace(state.AgentDResult))
            sb.AppendLine($"上一轮财务结论摘要: {Truncate(state.AgentDResult, 320)}");
        return sb.ToString().Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
