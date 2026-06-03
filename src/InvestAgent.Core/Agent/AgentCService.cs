using System.Text;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

public class AgentCService : ISubAgentService
{
    private readonly IStockDataService _stockDataService;
    private readonly IAgentPromptRunner _promptRunner;

    public string AgentName => "Agent C";

    public AgentCService(IStockDataService stockDataService, IAgentPromptRunner promptRunner)
    {
        _stockDataService = stockDataService;
        _promptRunner = promptRunner;
    }

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var newsMonths = task.NewsMonths ?? context.State.NewsMonths;
        var sentimentFilter = string.IsNullOrWhiteSpace(task.NewsSentimentFilter) ? context.State.NewsSentimentFilter : task.NewsSentimentFilter;

        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的新闻需求展开处理，窗口 {newsMonths} 个月，情绪过滤 {sentimentFilter}。"
        });

        var fetchCount = Math.Max(80, newsMonths * 60);
        var allNews = await _stockDataService.GetLatestNewsAsync(context.State.Symbol, fetchCount);
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_latest_news",
            FunctionArgs = $"{{\"symbol\":\"{context.State.Symbol}\",\"count\":{fetchCount}}}",
            Content = "抓取公司与行业新闻。"
        });

        var cutoff = DateTime.Today.AddMonths(-newsMonths);
        var filtered = allNews
            .Where(x => x.PublishTime >= cutoff)
            .OrderByDescending(x => x.PublishTime)
            .ToList();
        var company = filtered.Where(x => !(x.DataNote ?? "").Contains("行业新闻")).ToList();
        var industry = filtered.Where(x => (x.DataNote ?? "").Contains("行业新闻")).ToList();

        if (sentimentFilter == "positive")
        {
            company = company.Where(IsPositive).ToList();
            industry = industry.Where(IsPositive).ToList();
        }
        else if (sentimentFilter == "negative")
        {
            company = company.Where(IsNegative).ToList();
            industry = industry.Where(IsNegative).ToList();
        }

        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_latest_news",
            FunctionResult = $"公司新闻 {company.Count} 条, 行业新闻 {industry.Count} 条",
            Content = "新闻过滤完成。"
        });

        var narrative = await _promptRunner.RunPromptAsync(
            "你是 Agent C，负责新闻与事件分析。输出必须保留具体事件、情绪、公司与行业两层视角，并标注积极/消极因素。",
            BuildPrompt(context.State.Symbol, task.Instruction, newsMonths, sentimentFilter, company, industry),
            0.2,
            context.Memory,
            BuildStateSummary(context.State));

        result.NarrativeResult = narrative;
        result.StatePatch = new SessionStatePatch
        {
            NewsMonths = newsMonths,
            NewsSentimentFilter = sentimentFilter,
            CompanyNews = company,
            IndustryNews = industry,
            AgentCResult = narrative
        };
        result.WorkflowSteps.Add(new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent C 已完成新闻与情绪分析。"
        });
        return result;
    }

    private static string BuildPrompt(string symbol, string instruction, int newsMonths, string sentimentFilter, List<NewsItem> company, List<NewsItem> industry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"标的: {symbol}");
        sb.AppendLine($"用户要求: {instruction}");
        sb.AppendLine($"新闻窗口: 最近 {newsMonths} 个月, 情绪过滤: {sentimentFilter}");
        sb.AppendLine();
        sb.AppendLine("公司新闻:");
        foreach (var item in company.Take(15))
            sb.AppendLine($"{item.PublishTime:yyyy-MM-dd HH:mm} | {item.Title} | {item.Summary} | {item.Url}");
        sb.AppendLine();
        sb.AppendLine("行业新闻:");
        foreach (var item in industry.Take(15))
            sb.AppendLine($"{item.PublishTime:yyyy-MM-dd HH:mm} | {item.Title} | {item.Summary} | {item.Url}");
        sb.AppendLine();
        sb.AppendLine("请输出详细新闻分析，包括：公司面、行业面、积极因素、消极因素、对股价/预期的潜在影响。");
        return sb.ToString();
    }

    private static bool IsPositive(NewsItem item)
    {
        var source = $"{item.Title} {item.Summary}";
        var words = new[] { "增长", "回购", "中标", "突破", "增持", "签约", "分红", "创新高", "利好" };
        return item.Sentiment == "positive" || words.Any(source.Contains);
    }

    private static bool IsNegative(NewsItem item)
    {
        var source = $"{item.Title} {item.Summary}";
        var words = new[] { "下滑", "亏损", "减持", "风险", "处罚", "诉讼", "利空", "爆雷", "违约" };
        return item.Sentiment == "negative" || words.Any(source.Contains);
    }

    private static string BuildStateSummary(AnalysisSessionState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"当前股票: {state.Symbol} {state.StockName}");
        sb.AppendLine($"当前新闻窗口: {state.NewsMonths} 个月");
        sb.AppendLine($"当前新闻过滤: {state.NewsSentimentFilter}");
        sb.AppendLine($"当前公司新闻数: {state.CompanyNews.Count}");
        sb.AppendLine($"当前行业新闻数: {state.IndustryNews.Count}");
        if (!string.IsNullOrWhiteSpace(state.AgentCResult))
            sb.AppendLine($"上一轮新闻结论摘要: {Truncate(state.AgentCResult, 320)}");
        return sb.ToString().Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
