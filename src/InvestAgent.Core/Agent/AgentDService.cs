using System.Text;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

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

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var financialYears = task.FinancialYears ?? context.State.FinancialYears;
        var reportCount = Math.Max(4, financialYears * 4);

        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的财务请求展开分析，目标窗口 {financialYears} 年。"
        });

        var history = await _stockDataService.GetKeyMetricsHistoryAsync(context.State.Symbol, reportCount);
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_key_metrics_history",
            FunctionArgs = $"{{\"symbol\":\"{context.State.Symbol}\",\"maxReports\":{reportCount}}}",
            Content = "抓取财务序列并评估趋势。"
        });
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_key_metrics_history",
            FunctionResult = $"财务记录 {history.Count} 条",
            Content = "财务序列抓取完成。"
        });

        var narrative = await _promptRunner.RunPromptAsync(
            "你是 Agent D，负责财务分析。请围绕盈利能力、增长、负债、趋势一致性进行细致分析。",
            BuildPrompt(context.State.Symbol, task.Instruction, financialYears, history),
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
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent D 已完成财务趋势分析。"
        });
        return result;
    }

    private static string BuildPrompt(string symbol, string instruction, int financialYears, List<KeyMetrics> history)
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
        sb.AppendLine("请输出详细财务分析，包括：盈利能力、成长性、负债结构、趋势一致性、潜在风险。");
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
