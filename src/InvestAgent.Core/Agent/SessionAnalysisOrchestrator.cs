using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;

namespace InvestAgent.Core.Agent;

public class SessionAnalysisOrchestrator : ISessionAnalysisOrchestrator
{
    private readonly IStockDataService _stockDataService;
    private readonly IAgentPromptRunner _promptRunner;
    private readonly Dictionary<string, ISubAgentService> _subAgents;

    public SessionAnalysisOrchestrator(
        IStockDataService stockDataService,
        IAgentPromptRunner promptRunner,
        IEnumerable<ISubAgentService> subAgents)
    {
        _stockDataService = stockDataService;
        _promptRunner = promptRunner;
        _subAgents = subAgents.ToDictionary(x => NormalizeAgentKey(x.AgentName), x => x, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<AgentStep>> RunInitialAnalysisAsync(AgentSessionContext context, string targetInput, IAnalysisStreamingObserver? observer = null)
    {
        var turnIndex = GetNextTurnIndex(context);
        await AddConversationAsync(context, "user", targetInput, turnIndex, observer);
        var steps = new List<AgentStep>();
        await AppendStepAsync(context, steps, "Agent A", new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"为 {context.State.Symbol} 创建新的股票分析会话，并执行初始全量分析。"
        }, turnIndex, observer);

        var mainBusinessStep = new AgentStep
        {
            Type = AgentStepType.Action,
            Content = "Agent A 正在整理公司主要业务与行业定位。"
        };
        await AppendStepAsync(context, steps, "Agent A", mainBusinessStep, turnIndex, observer);

        await EnsureMainBusinessAsync(context, targetInput, observer);

        var mainBusinessDone = new AgentStep
        {
            Type = AgentStepType.Observation,
            Content = "公司主要业务整理完成。"
        };
        await AppendStepAsync(context, steps, "Agent A", mainBusinessDone, turnIndex, observer);

        var tasks = new List<SubAgentTask>
        {
            new() { Agent = "B", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始K线分析，使用当前默认区间。", DailyDays = context.State.DailyDays, MonthlyMonths = context.State.MonthlyMonths },
            new() { Agent = "C", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始新闻分析，使用当前默认窗口。", NewsMonths = context.State.NewsMonths, NewsSentimentFilter = context.State.NewsSentimentFilter },
            new() { Agent = "D", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始财务分析，使用当前默认窗口。", FinancialYears = context.State.FinancialYears }
        };

        steps.AddRange(await ExecuteTasksAsync(context, tasks, turnIndex, observer));
        await FinalizeAssistantResponseAsync(context, targetInput, tasks, steps, "初始分析", turnIndex, isInitialAnalysis: true, observer);
        return steps;
    }

    public async Task<List<AgentStep>> HandleChatAsync(AgentSessionContext context, string userMessage, IAnalysisStreamingObserver? observer = null)
    {
        var currentSymbol = context.State.Symbol;
        var mentionedCode = Regex.Match(userMessage, @"\b\d{6}\b").Value;
        if (!string.IsNullOrWhiteSpace(mentionedCode) && !string.Equals(mentionedCode, currentSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var rejectTurnIndex = GetNextTurnIndex(context);
            await AddConversationAsync(context, "user", userMessage, rejectTurnIndex, observer);
            var reject = new List<AgentStep>();
            var rejectText = $"当前会话绑定的是 {currentSymbol}。如果你想分析 {mentionedCode}，请从顶部新建该股票分析会话。";
            await AppendStepAsync(context, reject, "Agent A", new AgentStep
            {
                Type = AgentStepType.Response,
                Content = rejectText
            }, rejectTurnIndex, observer);
            await AddConversationAsync(context, "assistant", rejectText, rejectTurnIndex, observer);
            var rejectPatch = new SessionStatePatch
            {
                FinalResponse = rejectText,
                FinalRiskAdvice = rejectText
            };
            rejectPatch.ApplyTo(context.State);
            await NotifyStatePatchedAsync(context, rejectPatch, observer);
            AddWorkflowRun(context, "Agent A", rejectTurnIndex, reject);
            return reject;
        }

        var turnIndex = GetNextTurnIndex(context);
        await AddConversationAsync(context, "user", userMessage, turnIndex, observer);

        var steps = new List<AgentStep>();
        await AppendStepAsync(context, steps, "Agent A", new AgentStep
        {
            Type = AgentStepType.Thought,
            Content = $"Agent A 正在理解当前会话内的追问，并为 {currentSymbol} 规划后续分析任务。"
        }, turnIndex, observer);

        var plan = await BuildDispatchPlanAsync(context, userMessage);
        await AppendStepAsync(context, steps, "Agent A", new AgentStep
        {
            Type = AgentStepType.Action,
            Content = $"Agent A 调度结果：{plan.Mode}，任务数 {plan.Tasks.Count}。"
        }, turnIndex, observer);

        if (string.Equals(plan.Mode, "clarify", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Mode, "reject_switch", StringComparison.OrdinalIgnoreCase) ||
            plan.Tasks.Count == 0)
        {
            var assistantText = string.IsNullOrWhiteSpace(plan.UserFacingMessage)
                ? $"当前未能从你的追问中识别出明确的数据更新任务。请更具体一些，例如“看一年日K”“用缠论分析”“只看消极新闻”“看五年财务”。"
                : plan.UserFacingMessage;
            await AppendStepAsync(context, steps, "Agent A", new AgentStep { Type = AgentStepType.Response, Content = assistantText }, turnIndex, observer);
            await AddConversationAsync(context, "assistant", assistantText, turnIndex, observer);
            var clarifyPatch = new SessionStatePatch
            {
                FinalResponse = assistantText,
                FinalRiskAdvice = assistantText
            };
            clarifyPatch.ApplyTo(context.State);
            await NotifyStatePatchedAsync(context, clarifyPatch, observer);
            AddWorkflowRun(context, "Agent A", turnIndex, steps);
            return steps;
        }

        steps.AddRange(await ExecuteTasksAsync(context, plan.Tasks, turnIndex, observer));
        await FinalizeAssistantResponseAsync(context, userMessage, plan.Tasks, steps, "追问分析", turnIndex, isInitialAnalysis: false, observer);
        return steps;
    }

    private async Task EnsureMainBusinessAsync(AgentSessionContext context, string userIntent, IAnalysisStreamingObserver? observer)
    {
        var rawBusiness = await _stockDataService.GetMainBusinessAsync(context.State.Symbol);
        var stockName = context.State.StockName;
        if (string.IsNullOrWhiteSpace(stockName))
        {
            var metrics = await _stockDataService.GetKeyMetricsAsync(context.State.Symbol);
            stockName = metrics?.Name ?? context.State.Symbol;
        }

        var seedPatch = new SessionStatePatch
        {
            StockName = stockName,
            MainBusiness = rawBusiness
        };
        seedPatch.ApplyTo(context.State);
        await NotifyStatePatchedAsync(context, seedPatch, observer);

        var expanded = await _promptRunner.RunPromptStreamingAsync(
            "你是 Agent A。请根据给定主营业务资料，输出：1) 所属行业与板块 2) 一段简洁但专业的业务理解。不要输出风险提示。",
            $"标的: {context.State.Symbol} {stockName}\n原始主营业务资料:\n{rawBusiness}\n\n请输出一段适合股票分析首页展示的“公司主要业务”内容。",
            async partial =>
            {
                var patch = new SessionStatePatch
                {
                    StockName = stockName,
                    MainBusiness = partial
                };
                patch.ApplyTo(context.State);
                await NotifyStatePatchedAsync(context, patch, observer);
            },
            0.2,
            context.Memory,
            BuildStateSummary(context.State));

        var finalPatch = new SessionStatePatch
        {
            StockName = stockName,
            MainBusiness = string.IsNullOrWhiteSpace(expanded) ? rawBusiness : expanded
        };
        finalPatch.ApplyTo(context.State);
        await NotifyStatePatchedAsync(context, finalPatch, observer);
    }

    private async Task<AgentDispatchPlan> BuildDispatchPlanAsync(AgentSessionContext context, string userMessage)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"当前股票: {context.State.Symbol} {context.State.StockName}");
        prompt.AppendLine($"当前日K窗口: {context.State.DailyDays} 天");
        prompt.AppendLine($"当前月K窗口: {context.State.MonthlyMonths} 个月");
        prompt.AppendLine($"当前财务窗口: {context.State.FinancialYears} 年");
        prompt.AppendLine($"当前新闻窗口: {context.State.NewsMonths} 个月");
        prompt.AppendLine($"当前新闻情绪过滤: {context.State.NewsSentimentFilter}");
        prompt.AppendLine($"用户追问: {userMessage}");
        prompt.AppendLine();
        prompt.AppendLine("请仅输出 JSON，不要输出解释。格式：");
        prompt.AppendLine("""
            {
              "mode": "delegate|clarify|reject_switch",
              "userFacingMessage": "",
              "tasks": [
                {
                  "agent": "B|C|D",
                  "instruction": "string",
                  "useChanTheory": false,
                  "dailyDays": null,
                  "monthlyMonths": null,
                  "financialYears": null,
                  "newsMonths": null,
                  "newsSentimentFilter": "all|positive|negative"
                }
              ]
            }
            """);
        prompt.AppendLine("规则：");
        prompt.AppendLine("1. K线/技术/缠论/日K/月K/走势 -> Agent B");
        prompt.AppendLine("2. 新闻/公告/积极/消极/行业新闻 -> Agent C");
        prompt.AppendLine("3. 财务/年报/季度/五年财务 -> Agent D");
        prompt.AppendLine("4. 若用户请求当前会话之外的另一只股票，则 mode=reject_switch。");
        prompt.AppendLine("5. 若用户请求最近一年的日K，dailyDays=250。");
        prompt.AppendLine("6. 若用户请求五年的财务报告，financialYears=5。");
        prompt.AppendLine("7. 若提到缠论、分型、笔、中枢、背驰、买卖点，则 useChanTheory=true。");
        prompt.AppendLine("8. 若提到只看积极/只看消极新闻，则设置 newsSentimentFilter。");

        var raw = await _promptRunner.RunPromptAsync(
            "你是 Agent A 的调度器，只负责把中文追问转成结构化派单 JSON。",
            prompt.ToString(),
            0,
            context.Memory,
            BuildStateSummary(context.State));

        var plan = TryParseDispatchPlan(raw);
        plan = RefineDispatchPlan(plan, userMessage, context.State);
        if (plan.Tasks.Count > 0 || !string.IsNullOrWhiteSpace(plan.UserFacingMessage))
            return plan;

        return FallbackDispatchPlan(userMessage, context.State);
    }

    private async Task<List<AgentStep>> ExecuteTasksAsync(AgentSessionContext context, List<SubAgentTask> tasks, int triggerTurnIndex, IAnalysisStreamingObserver? observer)
    {
        var steps = new List<AgentStep>();
        foreach (var task in tasks)
        {
            var key = NormalizeAgentKey(task.Agent);
            if (!_subAgents.TryGetValue(key, out var service))
            {
                await AppendStepAsync(context, steps, "Agent A", new AgentStep
                {
                    Type = AgentStepType.Response,
                    Content = $"未找到可执行的子代理: {task.Agent}"
                }, triggerTurnIndex, observer);
                continue;
            }

            var subResult = await service.ExecuteAsync(context, task, observer, triggerTurnIndex);
            subResult.StatePatch.ApplyTo(context.State);
            await NotifyStatePatchedAsync(context, subResult.StatePatch, observer);
            steps.AddRange(subResult.WorkflowSteps);
            AddWorkflowRun(context, service.AgentName, triggerTurnIndex, subResult.WorkflowSteps);
        }
        return steps;
    }

    private async Task FinalizeAssistantResponseAsync(
        AgentSessionContext context,
        string userIntent,
        List<SubAgentTask> executedTasks,
        List<AgentStep> steps,
        string workflowLabel,
        int turnIndex,
        bool isInitialAnalysis,
        IAnalysisStreamingObserver? observer)
    {
        await AppendStepAsync(context, steps, "Agent A", new AgentStep
        {
            Type = AgentStepType.Action,
            Content = isInitialAnalysis
                ? "Agent A 正在生成最终风险提示与投资建议。"
                : "Agent A 正在生成本轮追问的补充结论与风险提示。"
        }, turnIndex, observer);

        var assistantMessage = await AddConversationAsync(context, "assistant", "正在生成回复...", turnIndex, observer, addToMemory: false);

        var advice = await _promptRunner.RunPromptStreamingAsync(
            isInitialAnalysis
                ? "你是 Agent A。请只输出“最终风险提示与投资建议”这一小节，不要复述K线、新闻、财务正文，不要加标题序号。"
                : "你是 Agent A。当前是会话内追问，请只针对本轮被更新的主题输出补充结论和风险提示，不要把无关板块重新总结一遍。",
            BuildRiskAdvicePrompt(context.State, userIntent, executedTasks, isInitialAnalysis),
            async partial =>
            {
                var patch = new SessionStatePatch
                {
                    FinalRiskAdvice = partial,
                    FinalResponse = BuildFinalResponse(context.State, partial, executedTasks, isInitialAnalysis, userIntent),
                    InitialAnalysisResponse = isInitialAnalysis
                        ? BuildFinalResponse(context.State, partial, executedTasks, true, userIntent)
                        : null
                };
                patch.ApplyTo(context.State);
                assistantMessage.Content = context.State.FinalResponse;
                await NotifyStatePatchedAsync(context, patch, observer);
                await NotifyMessageUpdatedAsync(context, assistantMessage, observer);
            },
            0.3,
            context.Memory,
            BuildStateSummary(context.State));

        var finalResponse = BuildFinalResponse(context.State, advice, executedTasks, isInitialAnalysis, userIntent);
        var finalPatch = new SessionStatePatch
        {
            FinalRiskAdvice = advice,
            FinalResponse = finalResponse,
            InitialAnalysisResponse = isInitialAnalysis || string.IsNullOrWhiteSpace(context.State.InitialAnalysisResponse)
                ? finalResponse
                : null
        };
        finalPatch.ApplyTo(context.State);
        assistantMessage.Content = context.State.FinalResponse;
        context.Memory.AddAssistantMessage(context.State.FinalResponse);
        await NotifyStatePatchedAsync(context, finalPatch, observer);
        await NotifyMessageUpdatedAsync(context, assistantMessage, observer);

        await AppendStepAsync(context, steps, "Agent A", new AgentStep
        {
            Type = AgentStepType.Response,
            Content = $"Agent A 已完成{workflowLabel}并更新当前会话。"
        }, turnIndex, observer);
        AddWorkflowRun(context, "Agent A", turnIndex, steps);
    }

    private static string BuildRiskAdvicePrompt(AnalysisSessionState state, string userIntent, List<SubAgentTask> executedTasks, bool isInitialAnalysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"标的: {state.Symbol} {state.StockName}");
        sb.AppendLine($"用户本轮需求: {userIntent}");
        if (isInitialAnalysis)
        {
            sb.AppendLine("公司主要业务:");
            sb.AppendLine(state.MainBusiness);
            sb.AppendLine("K线分析:");
            sb.AppendLine(state.AgentBResult);
            sb.AppendLine("新闻分析:");
            sb.AppendLine(state.AgentCResult);
            sb.AppendLine("财务分析:");
            sb.AppendLine(state.AgentDResult);
            sb.AppendLine("请基于以上内容，仅输出最终风险提示与投资建议，强调不确定性。");
            return sb.ToString();
        }

        var agents = executedTasks.Select(t => NormalizeAgentKey(t.Agent)).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (agents.Contains("B"))
        {
            sb.AppendLine("本轮更新主题: K线/技术分析");
            sb.AppendLine(state.AgentBResult);
        }
        if (agents.Contains("C"))
        {
            sb.AppendLine("本轮更新主题: 新闻分析");
            sb.AppendLine(state.AgentCResult);
        }
        if (agents.Contains("D"))
        {
            sb.AppendLine("本轮更新主题: 财务分析");
            sb.AppendLine(state.AgentDResult);
        }
        sb.AppendLine("请只围绕本轮更新主题输出简短结论和对应风险，不要扩展到未被要求的其他板块。");
        return sb.ToString();
    }

    private static string BuildFinalResponse(AnalysisSessionState state, string advice, List<SubAgentTask> executedTasks, bool isInitialAnalysis, string userIntent)
    {
        if (!isInitialAnalysis)
            return BuildScopedFollowUpResponse(state, advice, executedTasks, userIntent);

        var sb = new StringBuilder();
        sb.AppendLine("## 公司主要业务");
        sb.AppendLine(state.MainBusiness);
        if (!string.IsNullOrWhiteSpace(state.AgentBResult))
        {
            sb.AppendLine();
            sb.AppendLine("## K线分析");
            sb.AppendLine(state.AgentBResult);
        }
        if (!string.IsNullOrWhiteSpace(state.AgentCResult))
        {
            sb.AppendLine();
            sb.AppendLine("## 新闻分析");
            sb.AppendLine(state.AgentCResult);
        }
        if (!string.IsNullOrWhiteSpace(state.AgentDResult))
        {
            sb.AppendLine();
            sb.AppendLine("## 财务分析");
            sb.AppendLine(state.AgentDResult);
        }
        sb.AppendLine();
        sb.AppendLine("## 最终风险提示与投资建议");
        sb.AppendLine(advice);
        if (!sb.ToString().Contains("⚠️ 以上分析仅供参考"))
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ 以上分析仅供参考");
        }
        return sb.ToString().Trim();
    }

    private static string BuildScopedFollowUpResponse(AnalysisSessionState state, string advice, List<SubAgentTask> executedTasks, string userIntent)
    {
        var sb = new StringBuilder();
        var agents = executedTasks.Select(t => NormalizeAgentKey(t.Agent)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        sb.AppendLine($"## 本轮追问");
        sb.AppendLine(userIntent);

        if (agents.Count == 1 && agents[0].Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("## K线专项分析");
            sb.AppendLine(state.AgentBResult);
        }
        else if (agents.Count == 1 && agents[0].Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("## 新闻专项分析");
            sb.AppendLine(state.AgentCResult);
        }
        else if (agents.Count == 1 && agents[0].Equals("D", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("## 财务专项分析");
            sb.AppendLine(state.AgentDResult);
        }
        else
        {
            if (agents.Contains("B", StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("## K线专项分析");
                sb.AppendLine(state.AgentBResult);
            }
            if (agents.Contains("C", StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("## 新闻专项分析");
                sb.AppendLine(state.AgentCResult);
            }
            if (agents.Contains("D", StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("## 财务专项分析");
                sb.AppendLine(state.AgentDResult);
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 本轮补充结论与风险");
        sb.AppendLine(advice);
        if (!sb.ToString().Contains("⚠️ 以上分析仅供参考"))
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ 以上分析仅供参考");
        }
        return sb.ToString().Trim();
    }

    private static AgentDispatchPlan TryParseDispatchPlan(string raw)
    {
        try
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrWhiteSpace(json))
                return new AgentDispatchPlan();
            var plan = JsonSerializer.Deserialize<AgentDispatchPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return plan ?? new AgentDispatchPlan();
        }
        catch
        {
            return new AgentDispatchPlan();
        }
    }

    private static AgentDispatchPlan FallbackDispatchPlan(string userMessage, AnalysisSessionState state)
    {
        var plan = new AgentDispatchPlan { Mode = "delegate" };
        if (userMessage.Contains("缠"))
        {
            plan.Tasks.Add(new SubAgentTask
            {
                Agent = "B",
                Instruction = userMessage,
                UseChanTheory = true,
                DailyDays = state.DailyDays,
                MonthlyMonths = state.MonthlyMonths
            });
        }
        else if (userMessage.Contains("K") || userMessage.Contains("k") || userMessage.Contains("走势"))
        {
            plan.Tasks.Add(new SubAgentTask
            {
                Agent = "B",
                Instruction = userMessage,
                DailyDays = userMessage.Contains("一年") ? 250 : state.DailyDays,
                MonthlyMonths = state.MonthlyMonths
            });
        }
        else if (userMessage.Contains("新闻") || userMessage.Contains("积极") || userMessage.Contains("消极"))
        {
            plan.Tasks.Add(new SubAgentTask
            {
                Agent = "C",
                Instruction = userMessage,
                NewsMonths = userMessage.Contains("一年") ? 12 : state.NewsMonths,
                NewsSentimentFilter = userMessage.Contains("积极") ? "positive" : userMessage.Contains("消极") ? "negative" : "all"
            });
        }
        else if (userMessage.Contains("财务") || userMessage.Contains("年报") || userMessage.Contains("报告"))
        {
            plan.Tasks.Add(new SubAgentTask
            {
                Agent = "D",
                Instruction = userMessage,
                FinancialYears = userMessage.Contains("五年") ? 5 : state.FinancialYears
            });
        }
        else
        {
            plan.Mode = "clarify";
            plan.UserFacingMessage = "当前追问还不够具体。你可以直接说“看一年日K”“用缠论分析”“看五年财务”“只看消极新闻”。";
        }
        return plan;
    }

    private static AgentDispatchPlan RefineDispatchPlan(AgentDispatchPlan plan, string userMessage, AnalysisSessionState state)
    {
        if (plan.Mode.Equals("clarify", StringComparison.OrdinalIgnoreCase) ||
            plan.Mode.Equals("reject_switch", StringComparison.OrdinalIgnoreCase))
        {
            return plan;
        }

        var normalized = userMessage.ToLowerInvariant();
        var wantsKline = ContainsAny(normalized, "k线", "日k", "月k", "走势", "均线", "缠", "背驰", "中枢", "分型", "买卖点");
        var wantsNews = ContainsAny(normalized, "新闻", "公告", "消息", "舆情", "积极", "消极", "行业新闻");
        var wantsFinance = ContainsAny(normalized, "财务", "财报", "年报", "季报", "报告", "roe", "roa", "利润", "营收");

        var intendedAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (wantsKline) intendedAgents.Add("B");
        if (wantsNews) intendedAgents.Add("C");
        if (wantsFinance) intendedAgents.Add("D");

        if (intendedAgents.Count == 0)
            return plan;

        var filteredTasks = plan.Tasks
            .Where(t => intendedAgents.Contains(NormalizeAgentKey(t.Agent)))
            .ToList();

        if (filteredTasks.Count == 0)
            return FallbackDispatchPlan(userMessage, state);

            plan.Tasks = filteredTasks
            .GroupBy(t => NormalizeAgentKey(t.Agent), StringComparer.OrdinalIgnoreCase)
            .Select(g => MergeTasksForAgent(g.Key, g.ToList(), userMessage, state))
            .ToList();

        return plan;
    }

    private static SubAgentTask MergeTasksForAgent(string agent, List<SubAgentTask> tasks, string userMessage, AnalysisSessionState state)
    {
        var merged = new SubAgentTask
        {
            Agent = agent,
            Instruction = string.Join("\n", tasks.Select(t => t.Instruction).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            IsInitialAnalysis = tasks.Any(t => t.IsInitialAnalysis),
            UseChanTheory = tasks.Any(t => t.UseChanTheory),
            DailyDays = tasks.Select(t => t.DailyDays).LastOrDefault(x => x.HasValue),
            MonthlyMonths = tasks.Select(t => t.MonthlyMonths).LastOrDefault(x => x.HasValue),
            FinancialYears = tasks.Select(t => t.FinancialYears).LastOrDefault(x => x.HasValue),
            NewsMonths = tasks.Select(t => t.NewsMonths).LastOrDefault(x => x.HasValue),
            NewsSentimentFilter = tasks.Select(t => t.NewsSentimentFilter).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "all"
        };

        if (string.IsNullOrWhiteSpace(merged.Instruction))
            merged.Instruction = userMessage;

        var lower = userMessage.ToLowerInvariant();
        if (agent.Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            merged.DailyDays ??= lower.Contains("一年") && lower.Contains("日k") ? 250 : state.DailyDays;
            merged.MonthlyMonths ??= state.MonthlyMonths;
            merged.UseChanTheory |= ContainsAny(lower, "缠", "中枢", "背驰", "分型", "买卖点");
        }
        else if (agent.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            merged.NewsMonths ??= lower.Contains("一年") ? 12 : state.NewsMonths;
            if (ContainsAny(lower, "积极", "利好"))
                merged.NewsSentimentFilter = "positive";
            else if (ContainsAny(lower, "消极", "利空"))
                merged.NewsSentimentFilter = "negative";
            else if (string.IsNullOrWhiteSpace(merged.NewsSentimentFilter))
                merged.NewsSentimentFilter = state.NewsSentimentFilter;
        }
        else if (agent.Equals("D", StringComparison.OrdinalIgnoreCase))
        {
            merged.FinancialYears ??= lower.Contains("五年") ? 5 : state.FinancialYears;
        }

        return merged;
    }

    private static bool ContainsAny(string source, params string[] keywords)
    {
        return keywords.Any(source.Contains);
    }

    private static string BuildStateSummary(AnalysisSessionState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"当前股票: {state.Symbol} {state.StockName}");
        sb.AppendLine($"当前会话标题: {state.SessionTitle}");
        sb.AppendLine($"当前日K窗口: {state.DailyDays} 天");
        sb.AppendLine($"当前月K窗口: {state.MonthlyMonths} 个月");
        sb.AppendLine($"当前新闻窗口: {state.NewsMonths} 个月");
        sb.AppendLine($"当前财务窗口: {state.FinancialYears} 年");
        sb.AppendLine($"当前新闻过滤: {state.NewsSentimentFilter}");
        if (!string.IsNullOrWhiteSpace(state.MainBusiness))
            sb.AppendLine($"主营业务摘要: {Truncate(state.MainBusiness, 240)}");
        if (!string.IsNullOrWhiteSpace(state.AgentBResult))
            sb.AppendLine($"K线分析摘要: {Truncate(state.AgentBResult, 260)}");
        if (!string.IsNullOrWhiteSpace(state.AgentCResult))
            sb.AppendLine($"新闻分析摘要: {Truncate(state.AgentCResult, 260)}");
        if (!string.IsNullOrWhiteSpace(state.AgentDResult))
            sb.AppendLine($"财务分析摘要: {Truncate(state.AgentDResult, 260)}");
        return sb.ToString().Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return "";
        return raw[start..(end + 1)];
    }

    private static string NormalizeAgentKey(string agentName)
    {
        return agentName.Replace("Agent", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static async Task AppendStepAsync(
        AgentSessionContext context,
        List<AgentStep> steps,
        string agentName,
        AgentStep step,
        int turnIndex,
        IAnalysisStreamingObserver? observer)
    {
        steps.Add(step);
        if (observer is not null)
            await observer.OnStepAddedAsync(context, agentName, step, turnIndex);
    }

    private static async Task NotifyStatePatchedAsync(
        AgentSessionContext context,
        SessionStatePatch patch,
        IAnalysisStreamingObserver? observer)
    {
        if (observer is not null)
            await observer.OnStatePatchedAsync(context, patch);
    }

    private static async Task<SessionChatMessage> AddConversationAsync(
        AgentSessionContext context,
        string role,
        string content,
        int turnIndex,
        IAnalysisStreamingObserver? observer,
        bool addToMemory = true)
    {
        var message = new SessionChatMessage
        {
            Role = role,
            Content = content,
            CreatedAt = DateTime.Now,
            TurnIndex = turnIndex
        };
        context.Messages.Add(message);
        if (addToMemory && role == "user")
            context.Memory.AddUserMessage(content);
        else if (addToMemory && role == "assistant")
            context.Memory.AddAssistantMessage(content);
        if (observer is not null)
            await observer.OnMessageAddedAsync(context, message);
        return message;
    }

    private static async Task NotifyMessageUpdatedAsync(
        AgentSessionContext context,
        SessionChatMessage message,
        IAnalysisStreamingObserver? observer)
    {
        if (observer is not null)
            await observer.OnMessageUpdatedAsync(context, message);
    }

    private static int GetNextTurnIndex(AgentSessionContext context)
    {
        return context.Messages.Count == 0 ? 1 : context.Messages.Max(x => x.TurnIndex) + 1;
    }

    private static void AddWorkflowRun(AgentSessionContext context, string agentName, int turnIndex, List<AgentStep> steps)
    {
        context.WorkflowRuns.Add(new WorkflowRunRecord
        {
            AgentName = agentName,
            TriggerTurnIndex = turnIndex,
            CreatedAt = DateTime.Now,
            Steps = steps.Select(CloneStep).ToList()
        });
    }

    private static AgentStep CloneStep(AgentStep step)
    {
        return new AgentStep
        {
            StepNumber = step.StepNumber,
            Type = step.Type,
            Content = step.Content,
            FunctionName = step.FunctionName,
            FunctionArgs = step.FunctionArgs,
            FunctionResult = step.FunctionResult,
            Timestamp = step.Timestamp
        };
    }
}
