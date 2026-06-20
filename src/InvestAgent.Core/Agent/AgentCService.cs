using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task, IAnalysisStreamingObserver? observer = null, int triggerTurnIndex = 0)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var isInitialAnalysis = task.IsInitialAnalysis;
        var newsMonths = task.NewsMonths ?? context.State.NewsMonths;
        var sentimentFilter = string.IsNullOrWhiteSpace(task.NewsSentimentFilter) ? context.State.NewsSentimentFilter : task.NewsSentimentFilter;

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的新闻需求展开处理，窗口 {newsMonths} 个月，情绪过滤 {sentimentFilter}。"
        }, observer, triggerTurnIndex);

        var fetchCount = Math.Max(80, newsMonths * 60);
        var allNews = await _stockDataService.GetLatestNewsAsync(context.State.Symbol, fetchCount);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_latest_news",
            FunctionArgs = $"{{\"symbol\":\"{context.State.Symbol}\",\"count\":{fetchCount}}}",
            Content = "抓取公司与行业新闻。"
        }, observer, triggerTurnIndex);

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

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_latest_news",
            FunctionResult = $"公司新闻 {company.Count} 条, 行业新闻 {industry.Count} 条",
            Content = "新闻过滤完成。"
        }, observer, triggerTurnIndex);

        var dataPatch = new SessionStatePatch
        {
            NewsMonths = newsMonths,
            NewsSentimentFilter = sentimentFilter,
            CompanyNews = company,
            IndustryNews = industry
        };
        context.ApplyPatch(dataPatch);
        await NotifyStatePatchedAsync(context, dataPatch, observer);

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            Content = "Agent C 正在生成新闻与情绪分析。"
        }, observer, triggerTurnIndex);

        var progressText = BuildProgressText(context.State.Symbol, newsMonths, sentimentFilter, company.Count, industry.Count, isInitialAnalysis);
        var progressPatch = new SessionStatePatch
        {
            AgentCResult = progressText
        };
        context.ApplyPatch(progressPatch);
        await NotifyStatePatchedAsync(context, progressPatch, observer);

        string rawNarrative;
        string narrative;
        if (isInitialAnalysis)
        {
            rawNarrative = await _promptRunner.RunPromptStreamingAsync(
                "你是 Agent C，负责新闻与事件分析。你必须先做结构化归纳，再输出严格 JSON。不要输出 Markdown，不要输出解释性前后缀，不要省略字段。",
                BuildPrompt(context.State.Symbol, task.Instruction, newsMonths, sentimentFilter, company, industry, isInitialAnalysis),
                async _ =>
                {
                    var patch = new SessionStatePatch
                    {
                        AgentCResult = progressText
                    };
                    context.ApplyPatch(patch);
                    await NotifyStatePatchedAsync(context, patch, observer);
                },
                0.2,
                context.Memory,
                BuildStateSummary(context.State));

            narrative = BuildNarrative(context.State.Symbol, rawNarrative, company, industry, newsMonths, sentimentFilter);
        }
        else
        {
            rawNarrative = await _promptRunner.RunPromptStreamingAsync(
                "你是 Agent C，负责新闻与事件分析。当前是会话内追问，请优先直接回应用户最关心的新闻问题，可以自由发挥，但必须紧扣公司新闻、行业新闻和情绪传导逻辑。",
                BuildPrompt(context.State.Symbol, task.Instruction, newsMonths, sentimentFilter, company, industry, isInitialAnalysis),
                async partial =>
                {
                    var patch = new SessionStatePatch
                    {
                        AgentCResult = partial
                    };
                    context.ApplyPatch(patch);
                    await NotifyStatePatchedAsync(context, patch, observer);
                },
                0.2,
                context.Memory,
                BuildStateSummary(context.State));

            narrative = NormalizeFollowUpNarrative(rawNarrative, context.State.Symbol, newsMonths, sentimentFilter);
        }

        result.NarrativeResult = narrative;
        result.StatePatch = new SessionStatePatch
        {
            NewsMonths = newsMonths,
            NewsSentimentFilter = sentimentFilter,
            CompanyNews = company,
            IndustryNews = industry,
            AgentCResult = narrative
        };
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent C 已完成新闻与情绪分析。"
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

    private static string BuildPrompt(string symbol, string instruction, int newsMonths, string sentimentFilter, List<NewsItem> company, List<NewsItem> industry, bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"标的: {symbol}");
        sb.AppendLine($"用户要求: {instruction}");
        sb.AppendLine($"新闻窗口: 最近 {newsMonths} 个月, 情绪过滤: {sentimentFilter}");
        sb.AppendLine();
        sb.AppendLine("公司新闻原始资料:");
        foreach (var item in company.Take(18))
            sb.AppendLine($"[{item.PublishTime:yyyy-MM-dd HH:mm}] 来源={item.Source} | 标题={item.Title} | 摘要={item.Summary} | 链接={item.Url}");
        sb.AppendLine();
        sb.AppendLine("行业新闻原始资料:");
        foreach (var item in industry.Take(18))
            sb.AppendLine($"[{item.PublishTime:yyyy-MM-dd HH:mm}] 来源={item.Source} | 标题={item.Title} | 摘要={item.Summary} | 链接={item.Url}");
        sb.AppendLine();
        if (isInitialAnalysis)
        {
            sb.AppendLine("请只输出 JSON，字段必须完整，格式如下：");
            sb.AppendLine("""
{
  "companyView": [
    {
      "title": "string",
      "date": "yyyy-MM-dd HH:mm",
      "source": "string",
      "sentiment": "积极|中性|消极",
      "analysis": "string",
      "impact": "string"
    }
  ],
  "industryView": [
    {
      "title": "string",
      "date": "yyyy-MM-dd HH:mm",
      "source": "string",
      "sentiment": "积极|中性|消极",
      "analysis": "string",
      "impact": "string"
    }
  ],
  "positiveFactors": ["string"],
  "negativeFactors": ["string"],
  "customFocusResponse": "string",
  "summary": "string"
}
""");
            sb.AppendLine("规则：");
            sb.AppendLine("1. companyView 聚焦公司新闻中最重要的 3-6 条，必须具体到事件，不要泛泛而谈。");
            sb.AppendLine("2. industryView 聚焦行业或同行新闻中最重要的 3-6 条，必须解释对该股票的传导关系。");
            sb.AppendLine("3. sentiment 只能是 积极、中性、消极 三选一。");
            sb.AppendLine("4. analysis 和 impact 必须简洁专业，避免重复。");
            sb.AppendLine("5. positiveFactors 和 negativeFactors 各列 2-5 条，不要和事件标题机械重复。");
            sb.AppendLine("6. customFocusResponse 用来专门回应用户本轮追问的重点，可以更自由发挥，但必须紧扣用户问题。");
            sb.AppendLine("7. 如果用户要求更深入的情绪解读、行业传导、事件比较、利好/利空归因，都放进 customFocusResponse。");
            sb.AppendLine("8. 不要输出 ###、####、编号解释、前言、后记或任何 JSON 之外内容。");
        }
        else
        {
            sb.AppendLine("请直接输出适合追问场景的 Markdown 分析，不必强制套固定模板。");
            sb.AppendLine("要求：");
            sb.AppendLine("1. 优先直接回答用户当前追问，例如只看利空、比较行业传导、解释情绪变化。");
            sb.AppendLine("2. 可以自由发挥，用自然段或少量小标题组织内容。");
            sb.AppendLine("3. 需要具体引用新闻事件、时间、来源或影响逻辑，不能空泛。");
            sb.AppendLine("4. 不要为了追求完整性把首轮总览结构整篇重写。");
        }
        return sb.ToString();
    }

    private static string BuildNarrative(string symbol, string rawNarrative, List<NewsItem> company, List<NewsItem> industry, int newsMonths, string sentimentFilter)
    {
        var parsed = TryParseStructuredResult(rawNarrative);
        if (parsed is not null)
            return RenderStructuredMarkdown(symbol, parsed, newsMonths, sentimentFilter);

        return NormalizeFallbackMarkdown(rawNarrative, symbol, company, industry, newsMonths, sentimentFilter);
    }

    private static NewsAnalysisResult? TryParseStructuredResult(string raw)
    {
        try
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<NewsAnalysisResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static string RenderStructuredMarkdown(string symbol, NewsAnalysisResult result, int newsMonths, string sentimentFilter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {symbol} 新闻分析");
        sb.AppendLine();
        sb.AppendLine($"分析窗口：最近 {newsMonths} 个月");
        sb.AppendLine($"情绪过滤：{ToSentimentLabel(sentimentFilter)}");

        sb.AppendLine();
        sb.AppendLine("### 公司面");
        AppendEventSection(sb, result.CompanyView, "暂无可提炼的公司重点新闻。");

        sb.AppendLine();
        sb.AppendLine("### 行业面");
        AppendEventSection(sb, result.IndustryView, "暂无可提炼的行业重点新闻。");

        sb.AppendLine();
        sb.AppendLine("### 积极因素");
        AppendFactorSection(sb, result.PositiveFactors, "近期未识别出足够明确的积极因素。");

        sb.AppendLine();
        sb.AppendLine("### 消极因素");
        AppendFactorSection(sb, result.NegativeFactors, "近期未识别出足够明确的消极因素。");

        if (!string.IsNullOrWhiteSpace(result.CustomFocusResponse))
        {
            sb.AppendLine();
            sb.AppendLine("### 本轮追问聚焦");
            sb.AppendLine(result.CustomFocusResponse.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("### 小结");
        sb.AppendLine(string.IsNullOrWhiteSpace(result.Summary)
            ? "当前新闻面对该股票的影响以事件驱动为主，建议结合公告兑现情况与行业景气变化继续跟踪。"
            : result.Summary.Trim());
        return sb.ToString().Trim();
    }

    private static void AppendEventSection(StringBuilder sb, List<NewsEvent>? items, string emptyText)
    {
        if (items is null || items.Count == 0)
        {
            sb.AppendLine(emptyText);
            return;
        }

        foreach (var item in items.Where(HasMeaningfulEvent))
        {
            var title = SafeText(item.Title, "未命名事件");
            var date = SafeText(item.Date, "时间未标注");
            var source = SafeText(item.Source, "来源未标注");
            var sentiment = NormalizeSentiment(item.Sentiment);
            var analysis = SafeText(item.Analysis, "暂无分析。");
            var impact = SafeText(item.Impact, "影响有待继续观察。");
            sb.AppendLine($"- **{title}**（{date}，{source}，情绪：{sentiment}）：{analysis} 影响：{impact}");
        }
    }

    private static void AppendFactorSection(StringBuilder sb, List<string>? factors, string emptyText)
    {
        var valid = factors?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (valid.Count == 0)
        {
            sb.AppendLine($"- {emptyText}");
            return;
        }

        foreach (var item in valid)
            sb.AppendLine($"- {item}");
    }

    private static bool HasMeaningfulEvent(NewsEvent? item)
    {
        return item is not null &&
               (!string.IsNullOrWhiteSpace(item.Title) ||
                !string.IsNullOrWhiteSpace(item.Analysis) ||
                !string.IsNullOrWhiteSpace(item.Impact));
    }

    private static string NormalizeFallbackMarkdown(string rawNarrative, string symbol, List<NewsItem> company, List<NewsItem> industry, int newsMonths, string sentimentFilter)
    {
        var normalized = StripReasoningArtifacts(rawNarrative);
        if (LooksLikeRawJson(normalized))
            return RenderSourceBackedNewsMarkdown(symbol, company, industry, newsMonths, sentimentFilter);

        normalized = normalized.Replace("\r\n", "\n");
        normalized = Regex.Replace(normalized, @"(?<!\n)(#{2,6})", "\n$1");
        normalized = Regex.Replace(normalized, @"(?m)^(#{1,6})(\S)", "$1 $2");
        normalized = Regex.Replace(normalized, @"[•·]\s*", "- ");
        normalized = Regex.Replace(normalized, @"(?m)^(\d+)\.\s*", "- ");
        normalized = Regex.Replace(normalized, @"(?m)^(积极因素|消极因素|公司面分析|行业面分析|小结)\s*$", "### $1");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n").Trim();

        if (!normalized.StartsWith("## "))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## {symbol} 新闻分析");
            sb.AppendLine();
            sb.AppendLine($"分析窗口：最近 {newsMonths} 个月");
            sb.AppendLine($"情绪过滤：{ToSentimentLabel(sentimentFilter)}");
            sb.AppendLine();
            sb.AppendLine(normalized);

            if (company.Count == 0 && industry.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("### 小结");
                sb.AppendLine("当前没有足够的新闻样本，建议稍后重试或结合公告面继续跟踪。");
            }

            return sb.ToString().Trim();
        }

        return normalized;
    }

    private static string RenderSourceBackedNewsMarkdown(string symbol, List<NewsItem> company, List<NewsItem> industry, int newsMonths, string sentimentFilter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {symbol} 新闻分析");
        sb.AppendLine();
        sb.AppendLine($"分析窗口：最近 {newsMonths} 个月");
        sb.AppendLine($"情绪过滤：{ToSentimentLabel(sentimentFilter)}");

        sb.AppendLine();
        sb.AppendLine("### 公司面");
        AppendSourceNewsSection(sb, company, "暂无可提炼的公司重点新闻。");

        sb.AppendLine();
        sb.AppendLine("### 行业面");
        AppendSourceNewsSection(sb, industry, "暂无可提炼的行业重点新闻。");

        sb.AppendLine();
        sb.AppendLine("### 积极因素");
        AppendSourceFactors(sb, company.Concat(industry), IsPositive, "近期未识别出足够明确的积极因素。");

        sb.AppendLine();
        sb.AppendLine("### 消极因素");
        AppendSourceFactors(sb, company.Concat(industry), IsNegative, "近期未识别出足够明确的消极因素。");

        sb.AppendLine();
        sb.AppendLine("### 小结");
        if (company.Count == 0 && industry.Count == 0)
            sb.AppendLine("当前没有足够的新闻样本，建议稍后重试或结合公告面继续跟踪。");
        else
            sb.AppendLine("模型返回了未格式化的结构化中间结果，系统已改用已抓取新闻源生成可读摘要。后续建议继续结合公告兑现、行业景气和价格走势验证。");

        return sb.ToString().Trim();
    }

    private static void AppendSourceNewsSection(StringBuilder sb, IEnumerable<NewsItem> items, string emptyText)
    {
        var valid = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) || !string.IsNullOrWhiteSpace(x.Summary))
            .OrderByDescending(x => x.PublishTime)
            .Take(6)
            .ToList();

        if (valid.Count == 0)
        {
            sb.AppendLine(emptyText);
            return;
        }

        foreach (var item in valid)
        {
            var title = SafeText(item.Title, "未命名事件");
            var date = item.PublishTime == default ? "时间未标注" : item.PublishTime.ToString("yyyy-MM-dd");
            var source = SafeText(item.Source, "来源未标注");
            var sentiment = NormalizeSentiment(item.Sentiment);
            var summary = SafeText(string.IsNullOrWhiteSpace(item.Summary) ? item.Content : item.Summary, "暂无摘要。");
            sb.AppendLine($"- **{title}**（{date}，{source}，情绪：{sentiment}）：{Truncate(summary, 160)}");
        }
    }

    private static void AppendSourceFactors(StringBuilder sb, IEnumerable<NewsItem> items, Func<NewsItem, bool> classifier, string emptyText)
    {
        var factors = items
            .Where(classifier)
            .OrderByDescending(x => x.PublishTime)
            .Select(x =>
            {
                var title = SafeText(x.Title, "未命名事件");
                var summary = SafeText(string.IsNullOrWhiteSpace(x.Summary) ? x.Content : x.Summary, "暂无摘要。");
                return $"{title}：{Truncate(summary, 120)}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (factors.Count == 0)
        {
            sb.AppendLine($"- {emptyText}");
            return;
        }

        foreach (var factor in factors)
            sb.AppendLine($"- {factor}");
    }

    private static bool LooksLikeRawJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("{") ||
               trimmed.StartsWith("[") ||
               Regex.IsMatch(text, @"""(?:companyView|industryView|positiveFactors|negativeFactors|customFocusResponse|summary)""\s*:");
    }

    private static string StripReasoningArtifacts(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        const string tags = "think|thinking|analysis|reasoning|thought|thing";
        text = Regex.Replace(text, $@"(?is)<\s*(?:{tags})\b[^>]*>.*?<\s*/\s*(?:{tags})\s*>", "");
        text = Regex.Replace(text, $@"(?is)<\s*(?:{tags})\b[^>]*>.*$", "");

        var closes = Regex.Matches(text, $@"(?is)<\s*/\s*(?:{tags})\s*>");
        if (closes.Count > 0)
        {
            var last = closes[closes.Count - 1];
            var prefix = text[..last.Index];
            var suffix = text[(last.Index + last.Length)..];
            if (Regex.IsMatch(prefix, @"(?im)(用户要求|分析步骤|组织语言|当前数据|思考过程|推理过程|Chain\s*of\s*Thought|Reasoning)"))
                text = suffix;
            else
                text = Regex.Replace(text, $@"(?is)<\s*/\s*(?:{tags})\s*>", "");
        }

        return text.Trim();
    }

    private static string NormalizeFollowUpNarrative(string rawNarrative, string symbol, int newsMonths, string sentimentFilter)
    {
        var normalized = StripReasoningArtifacts(rawNarrative).Replace("\r\n", "\n");
        normalized = Regex.Replace(normalized, @"(?<!\n)(#{2,6})", "\n$1");
        normalized = Regex.Replace(normalized, @"(?m)^(#{1,6})(\S)", "$1 $2");
        normalized = Regex.Replace(normalized, @"[•·]\s*", "- ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n").Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"## 新闻追问分析\n\n分析窗口：最近 {newsMonths} 个月\n情绪过滤：{ToSentimentLabel(sentimentFilter)}\n\n当前没有生成有效的新闻追问分析。";
        }

        if (normalized.StartsWith("## "))
            return normalized;

        var sb = new StringBuilder();
        sb.AppendLine($"## {symbol} 新闻追问分析");
        sb.AppendLine();
        sb.AppendLine($"分析窗口：最近 {newsMonths} 个月");
        sb.AppendLine($"情绪过滤：{ToSentimentLabel(sentimentFilter)}");
        sb.AppendLine();
        sb.AppendLine(normalized);
        return sb.ToString().Trim();
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return "";
        return raw[start..(end + 1)];
    }

    private static string NormalizeSentiment(string? sentiment)
    {
        if (string.IsNullOrWhiteSpace(sentiment))
            return "中性";

        if (sentiment.Contains("积极", StringComparison.OrdinalIgnoreCase) || sentiment.Contains("positive", StringComparison.OrdinalIgnoreCase))
            return "积极";
        if (sentiment.Contains("消极", StringComparison.OrdinalIgnoreCase) || sentiment.Contains("negative", StringComparison.OrdinalIgnoreCase))
            return "消极";
        return "中性";
    }

    private static string ToSentimentLabel(string? sentimentFilter)
    {
        return sentimentFilter switch
        {
            "positive" => "仅积极",
            "negative" => "仅消极",
            _ => "全部"
        };
    }

    private static string SafeText(string? text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private static string BuildProgressText(string symbol, int newsMonths, string sentimentFilter, int companyCount, int industryCount, bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isInitialAnalysis ? $"## {symbol} 新闻分析" : $"## {symbol} 新闻追问分析");
        sb.AppendLine();
        sb.AppendLine($"分析窗口：最近 {newsMonths} 个月");
        sb.AppendLine($"情绪过滤：{ToSentimentLabel(sentimentFilter)}");
        sb.AppendLine();
        sb.AppendLine($"已抓取公司新闻 {companyCount} 条，行业新闻 {industryCount} 条。");
        sb.AppendLine("Agent C 正在整理公司面、行业面以及积极/消极因素，请稍候...");
        return sb.ToString().Trim();
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

    private sealed class NewsAnalysisResult
    {
        public List<NewsEvent> CompanyView { get; set; } = new();
        public List<NewsEvent> IndustryView { get; set; } = new();
        public List<string> PositiveFactors { get; set; } = new();
        public List<string> NegativeFactors { get; set; } = new();
        public string CustomFocusResponse { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    private sealed class NewsEvent
    {
        public string Title { get; set; } = "";
        public string Date { get; set; } = "";
        public string Source { get; set; } = "";
        public string Sentiment { get; set; } = "";
        public string Analysis { get; set; } = "";
        public string Impact { get; set; } = "";
    }
}
