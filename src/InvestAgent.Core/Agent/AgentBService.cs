using System.Text;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

/// <summary>
/// Agent B —— K线/技术结构分析服务。
/// 负责日K/月K数据抓取与缓存、缠论知识库检索、历史相似走势匹配，
/// 以及驱动 LLM 生成技术分析叙述（支持缠论多模态图片输入）。
/// </summary>
public class AgentBService : ISubAgentService
{
    private const int MaxChanImagesPerPrompt = 4;
    private const int DailyChartCacheDays = 1200;
    private const int MonthlyChartCacheMonths = 180;

    private readonly IStockDataService _stockDataService;
    private readonly ILocalKnowledgeService _localKnowledgeService;
    private readonly IHistoricalPatternService _historicalPatternService;
    private readonly IAgentPromptRunner _promptRunner;

    public string AgentName => "Agent B";

    public AgentBService(
        IStockDataService stockDataService,
        ILocalKnowledgeService localKnowledgeService,
        IHistoricalPatternService historicalPatternService,
        IAgentPromptRunner promptRunner)
    {
        _stockDataService = stockDataService;
        _localKnowledgeService = localKnowledgeService;
        _historicalPatternService = historicalPatternService;
        _promptRunner = promptRunner;
    }

    public async Task<SubAgentExecutionResult> ExecuteAsync(AgentSessionContext context, SubAgentTask task, IAnalysisStreamingObserver? observer = null, int triggerTurnIndex = 0)
    {
        var result = new SubAgentExecutionResult { AgentName = AgentName };
        var isInitialAnalysis = task.IsInitialAnalysis;
        var dailyDays = task.DailyDays ?? context.State.DailyDays;
        var monthlyMonths = task.MonthlyMonths ?? context.State.MonthlyMonths;
        var useChanTheory = task.UseChanTheory || ContainsChanKeywords(task.Instruction);
        var useHistoricalPatterns = ContainsHistoricalPatternKeywords(task.Instruction);

        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"围绕 {context.State.Symbol} 的K线请求展开分析，目标区间：日K {dailyDays} 天，月K {monthlyMonths} 个月。"
        }, observer, triggerTurnIndex);

        var dailyCacheTarget = Math.Max(dailyDays, DailyChartCacheDays);
        var monthlyCacheTarget = Math.Max(monthlyMonths, MonthlyChartCacheMonths);
        var dailyCache = HasEnoughKLines(context.State.DailyKLines, dailyCacheTarget)
            ? context.State.DailyKLines.OrderBy(x => x.Date).ToList()
            : await _stockDataService.GetHistoricalPricesAsync(context.State.Symbol, dailyCacheTarget);
        var monthlyCache = HasEnoughKLines(context.State.MonthlyKLines, monthlyCacheTarget)
            ? context.State.MonthlyKLines.OrderBy(x => x.Date).ToList()
            : await _stockDataService.GetMonthlyKLineAsync(context.State.Symbol, monthlyCacheTarget);
        var daily = TakeLatest(dailyCache, dailyDays);
        var monthly = TakeLatest(monthlyCache, monthlyMonths);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            FunctionName = "get_historical_prices/get_monthly_kline",
            Content = "准备K线缓存并切片用于技术分析。"
        }, observer, triggerTurnIndex);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Observation,
            FunctionName = "get_historical_prices/get_monthly_kline",
            FunctionResult = $"日K分析 {daily.Count} 条 / 缓存 {dailyCache.Count} 条, 月K分析 {monthly.Count} 条 / 缓存 {monthlyCache.Count} 条",
            Content = "K线数据抓取完成。"
        }, observer, triggerTurnIndex);

        var dataPatch = new SessionStatePatch
        {
            DailyDays = dailyDays,
            MonthlyMonths = monthlyMonths,
            DailyKLines = dailyCache,
            MonthlyKLines = monthlyCache
        };
        context.ApplyPatch(dataPatch);
        await NotifyStatePatchedAsync(context, dataPatch, observer);

        var historicalPatterns = new HistoricalPatternSearchResult();
        if (useHistoricalPatterns)
        {
            historicalPatterns = _historicalPatternService.SearchSimilarPatterns(context.State.Symbol, daily, task.Instruction, 8);
            await AppendStepAsync(context, result, new AgentStep
            {
                Type = AgentStepType.Observation,
                FunctionName = "search_historical_patterns",
                FunctionResult = BuildHistoricalPatternObservation(historicalPatterns),
                Content = $"历史相似走势检索完成：案例库 {historicalPatterns.TotalCaseCount} 条，命中 {historicalPatterns.MatchedCaseCount} 条。"
            }, observer, triggerTurnIndex);
        }

        var chanSections = new List<string>();
        var chanImages = new List<ChanImageResource>();
        var promptImages = new List<PromptImageInput>();
        if (useChanTheory)
        {
            chanSections = _localKnowledgeService.Search("chan", task.Instruction, 3);
            chanImages = _localKnowledgeService.SearchChanImages(task.Instruction, MaxChanImagesPerPrompt);
            promptImages = BuildPromptImages(chanImages);
            await AppendStepAsync(context, result, new AgentStep
            {
                Type = AgentStepType.Action,
                FunctionName = "search_local_knowledge",
                FunctionArgs = "{\"topic\":\"chan\"}",
                Content = "检索本地缠论知识库。"
            }, observer, triggerTurnIndex);
            await AppendStepAsync(context, result, new AgentStep
            {
                Type = AgentStepType.Observation,
                FunctionName = "search_local_knowledge",
                FunctionResult = chanSections.Count == 0 ? "未命中，回退到缠论模板。" : $"命中 {chanSections.Count} 个知识片段",
                Content = "缠论知识检索完成。"
            }, observer, triggerTurnIndex);
            await AppendStepAsync(context, result, new AgentStep
            {
                Type = AgentStepType.Observation,
                FunctionName = "search_chan_images",
                FunctionResult = chanImages.Count == 0
                    ? "No Chan image examples matched."
                    : string.Join("\n", chanImages.Select(x => $"{x.Id} | {x.Title} | {ResolveLocalResourcePath(x.LocalPath)}")),
                Content = $"Chan image examples matched: {chanImages.Count}; attached to model: {promptImages.Count}."
            }, observer, triggerTurnIndex);
        }

        var userPrompt = BuildPrompt(context.State.Symbol, task.Instruction, dailyDays, monthlyMonths, daily, monthly, useChanTheory, chanSections, chanImages, historicalPatterns, _localKnowledgeService.GetChanAnalysisTemplate(), isInitialAnalysis);
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Action,
            Content = useChanTheory ? "Agent B 正在结合缠论生成K线结构分析。" : "Agent B 正在生成K线结构分析。"
        }, observer, triggerTurnIndex);

        async Task OnPartialAsync(string partial)
        {
            var patch = new SessionStatePatch
            {
                AgentBResult = partial
            };
            context.ApplyPatch(patch);
            await NotifyStatePatchedAsync(context, patch, observer);
        }

        var narrative = useChanTheory
            ? await _promptRunner.RunPromptStreamingWithImagesAsync(
                BuildSystemPrompt(useChanTheory, isInitialAnalysis),
                userPrompt,
                promptImages,
                OnPartialAsync,
                0.2,
                context.Memory,
                BuildStateSummary(context.State))
            : await _promptRunner.RunPromptStreamingAsync(
                BuildSystemPrompt(useChanTheory, isInitialAnalysis),
                userPrompt,
                OnPartialAsync,
                0.2,
                context.Memory,
                BuildStateSummary(context.State));
        result.NarrativeResult = narrative;
        result.StatePatch = new SessionStatePatch
        {
            DailyDays = dailyDays,
            MonthlyMonths = monthlyMonths,
            DailyKLines = dailyCache,
            MonthlyKLines = monthlyCache,
            AgentBResult = narrative
        };
        await AppendStepAsync(context, result, new AgentStep
        {
            Type = AgentStepType.Response,
            Content = "Agent B 已完成K线与技术结构分析。"
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

    private static bool HasEnoughKLines(List<StockKLine> source, int requiredCount)
    {
        return source.Count >= requiredCount && source.Any(x => x.Date != default);
    }

    private static List<StockKLine> TakeLatest(List<StockKLine> source, int count)
    {
        return source
            .OrderBy(x => x.Date)
            .TakeLast(Math.Max(1, count))
            .ToList();
    }

    private static string BuildSystemPrompt(bool useChanTheory, bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是 Agent B，负责K线和技术结构分析。");
        sb.AppendLine("输出必须具体，优先引用区间涨跌、阶段高低点、结构变化、风险点。");
        sb.AppendLine("如果用户问的是某个局部主题，就只回答该主题，不要擅自扩展成整份股票总分析。");
        if (isInitialAnalysis)
            sb.AppendLine("这是首次总览分析，请适度结构化，方便用户快速建立全局认知。");
        else
            sb.AppendLine("这是会话内追问，请优先直接回应用户最关心的问题，允许自由展开论证，不必强行套固定模板。");
        if (useChanTheory)
        {
            sb.AppendLine("本次必须结合缠论进行分析，优先讨论级别、分型、笔、线段、中枢、背驰、买卖点。");
            sb.AppendLine("若数据不足以严谨判定某个缠论结论，必须明确说明不确定性。");
            sb.AppendLine("严禁只解释缠论概念，必须结合给定K线区间中的具体日期、价位和结构变化来分析。");
            sb.AppendLine("When Chan image examples are provided, inspect them as optional visual references. Do not include image markdown unless the user asks for images/diagrams or the image materially improves the explanation.");
        }
        sb.AppendLine("If historical pattern cases are provided, use them as empirical analogues only. Report sample size, similar-case outcomes, and key differences; never present them as deterministic prediction.");
        AppendMarkdownOutputRules(sb);
        return sb.ToString();
    }

    private static void AppendMarkdownOutputRules(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Markdown output rules:");
        sb.AppendLine("- Use clean Markdown only. Never output standalone heading markers such as #, ###, or ####.");
        sb.AppendLine("- Use headings as ## Title or ### Title, with a space after #. Do not glue heading text and paragraph text on the same line.");
        sb.AppendLine("- Use ordered lists as 1. item and bullet lists as - item. Do not output 1.item or heading- body.");
        sb.AppendLine("- Put a blank line between headings, paragraphs, lists, and images.");
        sb.AppendLine("- If you cite an image, put it on its own line as ![caption](absoluteLocalPath). Never output shorthand like !caption.");
        sb.AppendLine("- Do not reveal hidden reasoning, chain-of-thought, 思考过程, 推理过程, or internal planning. Only output the final user-facing analysis.");
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
        List<ChanImageResource> chanImages,
        HistoricalPatternSearchResult historicalPatterns,
        string chanTemplate,
        bool isInitialAnalysis)
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
            if (chanImages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Chan image examples available to inspect:");
                sb.AppendLine("The images are attached to this prompt in the same order as listed below when the model endpoint supports image input.");
                sb.AppendLine("Do not cite images by default. Cite one only when the user asks for images/diagrams, or when the image materially clarifies a Chan structure such as fractal, bi, segment, zhongshu, divergence, or buy/sell point.");
                sb.AppendLine("When citing an image in the answer, put Markdown image syntax on its own line and use one of the listed localPath values exactly, for example: ![short explanation](absoluteLocalPath)");
                foreach (var image in chanImages)
                {
                    var absolutePath = ResolveLocalResourcePath(image.LocalPath);
                    sb.AppendLine($"- [{image.Id}] {image.Title}");
                    sb.AppendLine($"  localPath: {absolutePath}");
                    sb.AppendLine($"  sourcePage: {image.PageUrl}");
                    sb.AppendLine($"  sourceImage: {image.ImageUrl}");
                    sb.AppendLine($"  tags: {string.Join(", ", image.Tags)}");
                    if (!string.IsNullOrWhiteSpace(image.ContextBefore))
                        sb.AppendLine($"  contextBefore: {Truncate(image.ContextBefore, 180)}");
                    if (!string.IsNullOrWhiteSpace(image.ContextAfter))
                        sb.AppendLine($"  contextAfter: {Truncate(image.ContextAfter, 220)}");
                }
            }
        }
        if (historicalPatterns.MatchedCaseCount > 0)
        {
            AppendHistoricalPatternPrompt(sb, historicalPatterns);
        }
        sb.AppendLine();
        if (useChanTheory)
        {
            if (isInitialAnalysis)
            {
                sb.AppendLine("请按较清晰的结构输出，并且每一节都尽量引用具体日期、价位或区间变化：");
                sb.AppendLine("1. 分析级别与区间");
                sb.AppendLine("2. 走势概览");
                sb.AppendLine("3. 分型与笔的识别");
                sb.AppendLine("4. 线段与中枢判断");
                sb.AppendLine("5. 背驰与买卖点判断");
                sb.AppendLine("6. 需要关注的风险与不确定性");
                sb.AppendLine("要求：不能只讲缠论定义，必须明确指出“哪些结构可以确认、哪些还不能确认”。");
            }
            else
            {
                sb.AppendLine("请围绕用户本轮追问自由发挥，但必须真正结合当前K线与缠论结构来回答。");
                sb.AppendLine("如果用户只关心某个局部问题，例如背驰、买卖点、中枢或最近一段走势，就聚焦那个问题深入分析。");
                sb.AppendLine("可以使用自然段或少量小标题，但不要被固定模板束缚。");
            }
        }
        else
        {
            if (isInitialAnalysis)
                sb.AppendLine("请输出相对结构化的首次总览分析，建议包含：走势概览、关键价位、结构判断、风险提示。");
            else
                sb.AppendLine("请围绕用户本轮问题自由分析，重点回答他追问的内容，不要为了完整性把整份K线总分析重写一遍。");
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

    private static void AppendHistoricalPatternPrompt(StringBuilder sb, HistoricalPatternSearchResult search)
    {
        var f = search.CurrentFeatures;
        var stats = search.OutcomeStats;
        sb.AppendLine();
        sb.AppendLine("历史相似走势检索结果:");
        sb.AppendLine(search.DataNote);
        sb.AppendLine("当前窗口量化特征:");
        sb.AppendLine($"- 区间涨跌幅: {f.ReturnPct:F2}%");
        sb.AppendLine($"- 最大回撤: {f.MaxDrawdownPct:F2}%");
        sb.AppendLine($"- 波动率: {f.VolatilityPct:F2}%");
        sb.AppendLine($"- 近20日量能比: {f.VolumeRatio20d:F2}");
        sb.AppendLine($"- MA20斜率: {f.Ma20SlopePct:F2}%；MA60斜率: {f.Ma60SlopePct:F2}%；均线结构: {f.MaArrangement}");
        sb.AppendLine($"- MACD状态: {f.MacdState}；RSI14: {f.Rsi14:F2}");
        sb.AppendLine($"- 收盘接近区间低位比例: {f.CloseNearLowPct:F2}%；是否跌破前低: {f.BreakPreviousLow}");
        sb.AppendLine();
        sb.AppendLine("Top相似案例聚合统计:");
        sb.AppendLine($"- 样本数: {stats.SampleSize}");
        sb.AppendLine($"- 20日上涨概率: {stats.Up20dRatePct:F2}%；60日上涨概率: {stats.Up60dRatePct:F2}%；120日上涨概率: {stats.Up120dRatePct:F2}%");
        sb.AppendLine($"- 60日内再次创新低概率: {stats.NewLowWithin60dRatePct:F2}%");
        sb.AppendLine($"- 20/60/120日收益中位数: {stats.MedianReturn20dPct:F2}% / {stats.MedianReturn60dPct:F2}% / {stats.MedianReturn120dPct:F2}%");
        sb.AppendLine($"- 60日最大回撤中位数: {stats.MedianMaxDrawdownNext60dPct:F2}%");
        if (stats.PatternTypeCounts.Count > 0)
            sb.AppendLine($"- 案例类型分布: {string.Join("；", stats.PatternTypeCounts.Select(x => $"{x.Key} {x.Value}个"))}");

        sb.AppendLine();
        sb.AppendLine("Top相似案例:");
        foreach (var match in search.Matches.Take(6))
        {
            var c = match.Case;
            var o = c.FutureOutcome;
            sb.AppendLine($"- 相似度 {match.SimilarityScore:F2}: {c.Title}");
            sb.AppendLine($"  行业/主题: {c.Industry}/{c.Theme}; 区间: {c.WindowStart:yyyy-MM-dd} ~ {c.WindowEnd:yyyy-MM-dd}; 类型: {c.PatternType}");
            sb.AppendLine($"  标签: {string.Join(", ", c.PatternLabels)}");
            sb.AppendLine($"  相似原因: {string.Join(", ", match.MatchReasons)}");
            sb.AppendLine($"  当时结构: {c.StructureSummary}");
            sb.AppendLine($"  后续验证: 20日 {o.Return20dPct:F2}%, 60日 {o.Return60dPct:F2}%, 120日 {o.Return120dPct:F2}%, 60日内再创新低={o.NewLowWithin60d}, 60日最大回撤={o.MaxDrawdownNext60dPct:F2}%");
            sb.AppendLine($"  教训: {c.Lesson}");
            sb.AppendLine($"  避免说法: {string.Join(", ", c.AvoidSaying)}");
        }
        sb.AppendLine();
        sb.AppendLine("回答要求: 必须说明当前走势更接近哪类历史结构、相似点、差异点、后续风险分布、需要哪些条件才能从下跌中继/弱修复切换为阶段底部。不要把历史相似走势说成预测。");
    }

    private static string BuildHistoricalPatternObservation(HistoricalPatternSearchResult search)
    {
        if (search.MatchedCaseCount == 0)
            return search.DataNote;

        var stats = search.OutcomeStats;
        var lines = new List<string>
        {
            $"当前特征: return={search.CurrentFeatures.ReturnPct:F2}%, maxDD={search.CurrentFeatures.MaxDrawdownPct:F2}%, ma={search.CurrentFeatures.MaArrangement}, macd={search.CurrentFeatures.MacdState}",
            $"样本数={stats.SampleSize}, up20={stats.Up20dRatePct:F2}%, up60={stats.Up60dRatePct:F2}%, up120={stats.Up120dRatePct:F2}%, newLow60={stats.NewLowWithin60dRatePct:F2}%",
            "Top matches:"
        };
        lines.AddRange(search.Matches.Take(5).Select(match =>
            $"{match.SimilarityScore:F2} | {match.Case.CaseId} | {match.Case.Title} | next60={match.Case.FutureOutcome.Return60dPct:F2}% | newLow60={match.Case.FutureOutcome.NewLowWithin60d}"));
        return string.Join("\n", lines);
    }

    private static List<PromptImageInput> BuildPromptImages(List<ChanImageResource> images)
    {
        return images
            .Select(image => new PromptImageInput
            {
                Id = image.Id,
                LocalPath = ResolveLocalResourcePath(image.LocalPath),
                MimeType = GetMimeType(image.LocalPath)
            })
            .Where(image => File.Exists(image.LocalPath))
            .ToList();
    }

    private static string ResolveLocalResourcePath(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return "";

        var normalized = localPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        var cwdCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalized));
        if (File.Exists(cwdCandidate))
            return cwdCandidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir.FullName, normalized));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return cwdCandidate;
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

    private static bool ContainsChanKeywords(string input)
    {
        var words = new[] { "缠论", "缠中说禅", "分型", "笔", "线段", "中枢", "背驰", "买卖点" };
        return words.Any(word => input.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsHistoricalPatternKeywords(string input)
    {
        var words = new[]
        {
            "历史相似", "相似走势", "相似案例", "历史案例", "案例库", "像不像",
            "阶段底部", "底部", "下跌中继", "弱反弹", "历史上", "后续怎么走",
            "概率", "胜率", "回测", "长期持有", "还能持有", "还能长期", "风险分布"
        };
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
