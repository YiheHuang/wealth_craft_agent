using System.Text;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

public class AgentBService : ISubAgentService
{
    private readonly IStockDataService _stockDataService;
    private readonly ILocalKnowledgeService _localKnowledgeService;
    private readonly IAgentPromptRunner _promptRunner;

    public string AgentName => "Agent B";

    public AgentBService(
        IStockDataService stockDataService,
        ILocalKnowledgeService localKnowledgeService,
        IAgentPromptRunner promptRunner)
    {
        _stockDataService = stockDataService;
        _localKnowledgeService = localKnowledgeService;
        _promptRunner = promptRunner;
    }

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var dailyDays = task.DailyDays ?? context.State.DailyDays;
        var monthlyMonths = task.MonthlyMonths ?? context.State.MonthlyMonths;
        var useChanTheory = task.UseChanTheory || ContainsChanKeywords(task.Instruction);

        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的K线请求展开分析，目标区间：日K {dailyDays} 天，月K {monthlyMonths} 个月。"
        });

        var daily = await _stockDataService.GetHistoricalPricesAsync(context.State.Symbol, dailyDays);
        var monthly = await _stockDataService.GetMonthlyKLineAsync(context.State.Symbol, monthlyMonths);
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_historical_prices/get_monthly_kline",
            Content = "抓取K线数据并准备技术分析。"
        });
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_historical_prices/get_monthly_kline",
            FunctionResult = $"日K {daily.Count} 条, 月K {monthly.Count} 条",
            Content = "K线数据抓取完成。"
        });

        var chanSections = new List<string>();
        if (useChanTheory)
        {
            chanSections = _localKnowledgeService.Search("chan", task.Instruction, 3);
            result.WorkflowSteps.Add(new AgentStep
            {
                Type = AgentStepType.Action,
                FunctionName = "search_local_knowledge",
                FunctionArgs = "{\"topic\":\"chan\"}",
                Content = "检索本地缠论知识库。"
            });
            result.WorkflowSteps.Add(new AgentStep
            {
                Type = AgentStepType.Observation,
                FunctionName = "search_local_knowledge",
                FunctionResult = chanSections.Count == 0 ? "未命中，回退到缠论模板。" : $"命中 {chanSections.Count} 个知识片段",
                Content = "缠论知识检索完成。"
            });
        }

        var userPrompt = BuildPrompt(context.State.Symbol, task.Instruction, dailyDays, monthlyMonths, daily, monthly, useChanTheory, chanSections, _localKnowledgeService.GetChanAnalysisTemplate());
        var narrative = await _promptRunner.RunPromptAsync(
            BuildSystemPrompt(useChanTheory),
            userPrompt,
            0.2,
            context.Memory,
            BuildStateSummary(context.State));
        result.NarrativeResult = narrative;
        result.StatePatch = new SessionStatePatch
        {
            DailyDays = dailyDays,
            MonthlyMonths = monthlyMonths,
            DailyKLines = daily,
            MonthlyKLines = monthly,
            AgentBResult = narrative
        };
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent B 已完成K线与技术结构分析。"
        });
        return result;
    }

    private static string BuildSystemPrompt(bool useChanTheory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是 Agent B，负责K线和技术结构分析。");
        sb.AppendLine("输出必须具体，优先引用区间涨跌、阶段高低点、结构变化、风险点。");
        sb.AppendLine("如果用户问的是某个局部主题，就只回答该主题，不要擅自扩展成整份股票总分析。");
        if (useChanTheory)
        {
            sb.AppendLine("本次必须结合缠论进行分析，优先讨论级别、分型、笔、线段、中枢、背驰、买卖点。");
            sb.AppendLine("若数据不足以严谨判定某个缠论结论，必须明确说明不确定性。");
            sb.AppendLine("严禁只解释缠论概念，必须结合给定K线区间中的具体日期、价位和结构变化来分析。");
        }
        return sb.ToString();
    }

    private static string BuildPrompt(
        string symbol,
        string instruction,
        int dailyDays,
        int monthlyMonths,
        List<StockKLine> daily,
        List<StockKLine> monthly,
        bool useChanTheory,
        List<string> chanSections,
        string chanTemplate)
    {
        var sb = new StringBuilder();
        var dailyStats = BuildStats(daily);
        var monthlyStats = BuildStats(monthly);
        sb.AppendLine($"标的: {symbol}");
        sb.AppendLine($"用户要求: {instruction}");
        sb.AppendLine($"日K窗口: {dailyDays} 天, 月K窗口: {monthlyMonths} 个月");
        sb.AppendLine();
        sb.AppendLine("日K关键统计:");
        sb.AppendLine(dailyStats);
        sb.AppendLine();
        sb.AppendLine("月K关键统计:");
        sb.AppendLine(monthlyStats);
        sb.AppendLine();
        sb.AppendLine("日K摘要:");
        foreach (var item in daily.TakeLast(Math.Min(90, daily.Count)))
            sb.AppendLine($"{item.Date:yyyy-MM-dd},{item.Open:F2},{item.High:F2},{item.Low:F2},{item.Close:F2},{item.Volume:F0}");
        sb.AppendLine();
        sb.AppendLine("月K摘要:");
        foreach (var item in monthly.TakeLast(Math.Min(12, monthly.Count)))
            sb.AppendLine($"{item.Date:yyyy-MM-dd},{item.Open:F2},{item.High:F2},{item.Low:F2},{item.Close:F2},{item.Volume:F0}");
        if (useChanTheory)
        {
            sb.AppendLine();
            sb.AppendLine("缠论标准模板:");
            sb.AppendLine(chanTemplate);
            if (chanSections.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("缠论知识片段:");
                foreach (var section in chanSections)
                    sb.AppendLine(section);
            }
        }
        sb.AppendLine();
        if (useChanTheory)
        {
            sb.AppendLine("请严格按以下结构输出，并且每一节都尽量引用具体日期、价位或区间变化：");
            sb.AppendLine("1. 分析级别与区间");
            sb.AppendLine("2. 最近三个月日K的走势概览");
            sb.AppendLine("3. 分型与笔的识别");
            sb.AppendLine("4. 线段与中枢判断");
            sb.AppendLine("5. 背驰与买卖点判断");
            sb.AppendLine("6. 需要关注的风险与不确定性");
            sb.AppendLine("要求：不能只讲缠论定义，必须明确指出“哪些结构可以确认、哪些还不能确认”。");
        }
        else
        {
            sb.AppendLine("请输出详细分析，不要只给结论。建议包含：走势概览、关键价位、结构判断、风险提示。");
        }
        return sb.ToString();
    }

    private static string BuildStats(List<StockKLine> klines)
    {
        if (klines.Count == 0)
            return "无数据";

        var ordered = klines.OrderBy(x => x.Date).ToList();
        var first = ordered.First();
        var last = ordered.Last();
        var high = ordered.MaxBy(x => x.High)!;
        var low = ordered.MinBy(x => x.Low)!;
        var upDays = 0;
        var downDays = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Close >= ordered[i - 1].Close) upDays++;
            else downDays++;
        }
        var changePct = first.Close == 0 ? 0 : (last.Close - first.Close) / first.Close * 100;
        return $"区间: {first.Date:yyyy-MM-dd} ~ {last.Date:yyyy-MM-dd}；期初收盘: {first.Close:F2}；期末收盘: {last.Close:F2}；区间涨跌幅: {changePct:+0.00;-0.00;0.00}%；最高价: {high.High:F2}({high.Date:yyyy-MM-dd})；最低价: {low.Low:F2}({low.Date:yyyy-MM-dd})；上涨周期数: {upDays}；下跌周期数: {downDays}";
    }

    private static bool ContainsChanKeywords(string input)
    {
        var words = new[] { "缠论", "缠中说禅", "分型", "笔", "线段", "中枢", "背驰", "买卖点" };
        return words.Any(word => input.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildStateSummary(AnalysisSessionState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"当前股票: {state.Symbol} {state.StockName}");
        sb.AppendLine($"当前日K窗口: {state.DailyDays} 天");
        sb.AppendLine($"当前月K窗口: {state.MonthlyMonths} 个月");
        if (state.DailyKLines.Count > 0)
        {
            var ordered = state.DailyKLines.OrderBy(x => x.Date).ToList();
            sb.AppendLine($"当前缓存日K区间: {ordered.First().Date:yyyy-MM-dd} ~ {ordered.Last().Date:yyyy-MM-dd}");
        }
        if (!string.IsNullOrWhiteSpace(state.AgentBResult))
            sb.AppendLine($"上一轮K线结论摘要: {Truncate(state.AgentBResult, 320)}");
        return sb.ToString().Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
