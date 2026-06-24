# WealthCraftAgent 答辩准备文档

成员：2352048 夏浩博、2351269 黄一和

本文档按“老师可能提问 -> 代码位置 -> 关键代码片段 -> 回答要点”组织，便于答辩时直接结合源码解释。

## 1. 什么是 ReAct？项目中哪里体现 Thought / Action / Observation？

老师可能问法：请解释 ReAct 模式，以及你们代码中哪里体现了这个循环。

代码位置：`InvestAgent.Core/Agent/InvestAgentLoop.cs`，约第 58-164 行。

关键代码片段：

```csharp
for (int step = 1; step <= _options.MaxSteps; step++)
{
    var thoughtStep = new AgentStep
    {
        StepNumber = step,
        Type = AgentStepType.Thought,
        Content = step == 1
            ? "分析用户问题，判断需要调用哪些工具..."
            : "根据已有数据，判断是否需要更多信息..."
    };
    ...
    foreach (var functionCall in functionCalls)
    {
        var actionStep = new AgentStep { Type = AgentStepType.Action };
        var result = await functionCall.InvokeAsync(_kernel);
        var obsStep = new AgentStep { Type = AgentStepType.Observation };
        _memory.AddToolMessage(resultStr, functionCall.FunctionName);
    }
}
```

回答要点：

- ReAct 是 Reasoning + Acting，核心流程是 Thought -> Action -> Observation，循环直到 Response。
- `Thought` 不是最终答案，而是当前步骤状态，便于 UI 展示 Agent 正在分析什么。
- `Action` 对应模型请求的工具调用，代码中通过 `functionCall.InvokeAsync(_kernel)` 执行。
- `Observation` 是工具返回结果，写入 `ConversationMemory` 后供下一轮 LLM 继续判断。

## 2. `InvestAgentLoop.RunAsync` 的完整流程是什么？

老师可能问法：从用户输入开始，讲一遍核心循环如何运行。

代码位置：`InvestAgent.Core/Agent/InvestAgentLoop.cs`，约第 45-176 行。

关键代码片段：

```csharp
public async IAsyncEnumerable<AgentStep> RunAsync(string userMessage)
{
    _memory.AddUserMessage(userMessage);

    var settings = new OpenAIPromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        Temperature = 0.7,
        MaxTokens = 4096
    };

    for (int step = 1; step <= _options.MaxSteps; step++)
    {
        response = await _chatService.GetChatMessageContentAsync(
            _memory.GetChatHistory(), settings, _kernel);
        ...
    }
}
```

回答要点：

- 用户输入先进入 `_memory`，保证 LLM 能看到本轮问题。
- `FunctionChoiceBehavior.Auto()` 允许模型从 Kernel 注册的函数中自动选择工具。
- `IAsyncEnumerable<AgentStep>` 让 UI 能边执行边显示步骤，而不是等待所有分析结束。
- 如果模型没有函数调用，就说明任务完成，代码保存助手回复并 `yield break`。
- 如果达到 `MaxSteps`，会返回保护性提示，防止无限循环。

## 3. 为什么使用 Semantic Kernel，而不是直接 HTTP 调用 LLM？

老师可能问法：Semantic Kernel 在项目里具体发挥了什么作用？

代码位置：`InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs`，约第 87-119 行。

关键代码片段：

```csharp
var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion(
    modelId: options.ModelId,
    apiKey: options.ApiKey,
    httpClient: httpClient,
    endpoint: new Uri(options.Endpoint));

builder.Plugins.AddFromObject(sp.GetRequiredService<StockPricePlugin>(), "StockPrice");
builder.Plugins.AddFromObject(sp.GetRequiredService<FinancialReportPlugin>(), "FinancialReport");
builder.Plugins.AddFromObject(sp.GetRequiredService<MarketNewsPlugin>(), "MarketNews");
builder.Plugins.AddFromObject(sp.GetRequiredService<TechnicalAnalysisPlugin>(), "TechnicalAnalysis");
builder.Plugins.AddFromObject(sp.GetRequiredService<LocalKnowledgePlugin>(), "LocalKnowledge");
```

回答要点：

- Semantic Kernel 统一负责 LLM 接入、Kernel 创建和插件注册。
- `AddFromObject` 把 C# 插件对象变成模型可调用的工具集合。
- 如果直接 HTTP 调用，需要手写工具 JSON Schema、函数路由和工具结果回填，复杂度更高。
- 本项目使用 SK 更能体现 .NET 技术栈深度，也更符合课程的 Tool Calling 要求。

## 4. 五个插件分别解决什么问题？如何证明工具超过 3 个？

老师可能问法：项目有哪些自定义工具？如何注册和测试？

代码位置：`InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs` 与 `InvestAgent.Tests/AgentLoopTests.cs`。

关键代码片段：

```csharp
services.AddSingleton<StockPricePlugin>();
services.AddSingleton<FinancialReportPlugin>();
services.AddSingleton<MarketNewsPlugin>();
services.AddSingleton<TechnicalAnalysisPlugin>();
services.AddSingleton<LocalKnowledgePlugin>();
```

```csharp
Assert.Contains("StockPrice", pluginNames);
Assert.Contains("FinancialReport", pluginNames);
Assert.Contains("MarketNews", pluginNames);
Assert.Contains("TechnicalAnalysis", pluginNames);
```

回答要点：

- `StockPrice` 提供价格、历史 K 线、月 K、股票搜索、资金流。
- `FinancialReport` 提供关键财务指标和利润分析。
- `MarketNews` 提供新闻和市场情绪。
- `TechnicalAnalysis` 提供 MA、RSI、MACD、交易信号。
- `LocalKnowledge` 提供缠论知识库检索。
- 测试中检查插件名和函数名，证明工具注册可被 Kernel 发现。

## 5. Agent A/B/C/D 的职责边界是什么？

老师可能问法：为什么说这是 Multi-Agent，而不是一个大 Prompt？

代码位置：`InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs`，约第 30-56 行；`InvestAgent.Core/Agent/AgentBService.cs`、`InvestAgent.Core/Agent/AgentCService.cs`、`InvestAgent.Core/Agent/AgentDService.cs`。

关键代码片段：

```csharp
var tasks = new List<SubAgentTask>
{
    new() { Agent = "B", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始K线分析..." },
    new() { Agent = "C", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始新闻分析..." },
    new() { Agent = "D", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始财务分析..." }
};

steps.AddRange(await ExecuteInitialTasksInParallelAsync(context, tasks, turnIndex, observer));
await FinalizeAssistantResponseAsync(...);
```

回答要点：

- Agent A 是编排者，负责创建会话、派发任务、整合结果和生成最终风险提示。
- Agent B 专注 K 线、技术结构、缠论 RAG 和历史趋势 RAG。
- Agent C 专注公司新闻、行业新闻和情绪因素。
- Agent D 专注财务指标序列和趋势判断。
- 这样拆分后，每个 Agent 的输入数据、提示词和输出目标都更稳定，避免一个大 Prompt 同时处理所有复杂任务。

## 6. 多轮追问如何识别用户意图并派单？

老师可能问法：用户追问“只看消极新闻”或“用缠论分析”时，系统怎么知道调哪个 Agent？

代码位置：`InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs`，约第 178-229 行与第 405-485 行。

关键代码片段：

```csharp
prompt.AppendLine("1. K线/技术/缠论/日K/月K/走势 -> Agent B");
prompt.AppendLine("2. 新闻/公告/积极/消极/行业新闻 -> Agent C");
prompt.AppendLine("3. 财务/年报/季度/五年财务 -> Agent D");
prompt.AppendLine("8. 若提到缠论、分型、笔、中枢、背驰、买卖点，则 useChanTheory=true。");
```

```csharp
var wantsKline = ContainsAny(normalized, "k线", "日k", "月k", "走势", "均线", "缠", "背驰", "中枢");
var wantsNews = ContainsAny(normalized, "新闻", "公告", "消息", "舆情", "积极", "消极", "行业新闻");
var wantsFinance = ContainsAny(normalized, "财务", "财报", "年报", "季报", "roe", "利润", "营收");
```

回答要点：

- 首先由 Agent A 调用 LLM，把自然语言追问转成结构化 JSON 派单。
- 然后 `RefineDispatchPlan` 用关键词做二次校验，避免 LLM 派错 Agent。
- 缠论、背驰、中枢等词会派给 Agent B；积极/消极新闻派给 Agent C；财务、年报、ROE 等派给 Agent D。
- 如果用户要求切换股票，会拒绝并提示新建会话，避免当前上下文污染。

## 7. Memory 是如何保存上下文并避免过长的？

老师可能问法：上下文是怎么维护的？会不会无限增长？

代码位置：`InvestAgent.Core/Memory/ConversationMemory.cs`，约第 35-92 行。

关键代码片段：

```csharp
public void AddUserMessage(string message)
{
    _chatHistory.AddUserMessage(message);
    _turnCount++;
    TrimIfNeeded();
}

public void AddToolMessage(string content, string functionName)
{
    _chatHistory.Add(new ChatMessageContent(
        AuthorRole.Tool,
        content,
        metadata: new Dictionary<string, object?> { ["functionName"] = functionName }));
}
```

```csharp
private void TrimIfNeeded()
{
    if (_turnCount > _maxTurns)
        Trim(_maxTurns);
}
```

回答要点：

- 用户消息、助手回复和工具结果都进入 `ChatHistory`。
- 工具消息使用 `AuthorRole.Tool`，并在 metadata 中记录函数名。
- 当对话轮次超过最大值时，保留系统消息和最近若干轮，避免上下文无限膨胀。
- 结构化状态不完全依赖聊天历史，而是保存在 `AnalysisSessionState`，便于 UI 展示和历史回放。

## 8. 缠论 RAG 与历史趋势 RAG 如何实现？局限是什么？

老师可能问法：你们的 RAG 数据从哪里来？是不是向量数据库？

代码位置：`InvestAgent.Core/Services/LocalKnowledgeService.cs` 与 `InvestAgent.Core/Agent/AgentBService.cs`。

关键代码片段：

```csharp
var files = Directory.GetFiles(_docsRoot, "chan_theory*.md", SearchOption.TopDirectoryOnly);
return files.Where(File.Exists).Select(File.ReadAllText).ToList();
```

```csharp
chanSections = _localKnowledgeService.Search("chan", task.Instruction, 3);
chanImages = _localKnowledgeService.SearchChanImages(task.Instruction, MaxChanImagesPerPrompt);
historicalPatterns = _historicalPatternService.SearchSimilarPatterns(
    context.State.Symbol, daily, task.Instruction, 8);
```

回答要点：

- 缠论 RAG 从 `docs/chan_theory*.md` 与 `docs/chan_images/manifest.json` 加载本地资料。
- 检索方式主要是关键词、标签和文档段落评分。
- 历史趋势 RAG 使用本地案例库，根据当前 K 线特征匹配相似案例。
- 目前不是向量数据库，优点是离线稳定、可解释，缺点是语义泛化能力不如向量检索。
- 输出必须说明样本量、相似点、差异点和不确定性，不能把历史相似走势当成预测。

## 9. `async/await` 在项目中起什么作用？

老师可能问法：你们哪里体现了异步编程？

代码位置：`InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs` 与 `InvestAgent.Core/Agent/AgentPromptRunner.cs`。

关键代码片段：

```csharp
var runs = tasks
    .Select((task, index) => ExecuteSubAgentTaskAsync(context, task, index, triggerTurnIndex, observer))
    .ToArray();

var results = await Task.WhenAll(runs);
```

```csharp
await foreach (var chunk in _chatCompletionService.GetStreamingChatMessageContentsAsync(history, settings))
{
    builder.Append(chunk.Content);
    await onPartial(SanitizeAssistantOutput(builder.ToString(), allowPartial: true));
}
```

回答要点：

- 首轮分析中 B/C/D 可以并行执行，减少等待时间。
- LLM 流式输出通过 `await foreach` 接收片段，并逐步推送给 UI。
- 数据抓取、历史保存、会话处理都用异步方法，避免阻塞 WPF 界面。
- `RunAsync` 返回异步枚举，使步骤流能够边运行边展示。

## 10. 失败场景如何处理？AI 辅助开发如何透明说明？

老师可能问法：接口失败怎么办？哪些代码由 AI 辅助完成，你们怎么保证理解？

代码位置：`InvestAgent.Core/Agent/InvestAgentLoop.cs`、`InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs`、`InvestAgent.Core/Agent/AgentPromptRunner.cs`。

关键代码片段：

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "LLM 调用失败 (Step {Step})", step);
    errorMessage = $"抱歉，分析过程中出现错误：{ex.Message}";
}
```

```csharp
catch (Exception ex)
{
    var failureStep = new AgentStep
    {
        Type = AgentStepType.Response,
        Content = $"{service.AgentName} 执行失败，已跳过该模块并继续后续分析。原因：{BuildExceptionSummary(ex)}"
    };
}
```

回答要点：

- LLM 失败时返回用户可读错误，不让程序直接崩溃。
- 工具或子 Agent 失败时记录步骤，跳过失败模块并继续其他分析。
- `AgentPromptRunner` 对短暂网络错误做重试，并过滤模型输出中的推理草稿。
- AI 参与了大量辅助，包括代码生成、局部重构、文档起草、测试建议和错误排查建议。
- 成员负责需求判断、运行验证、调试集成和最终理解；核心代码已逐行整理，答辩时能解释字段、分支、异常处理和设计取舍。

## 11. 多 Agent 架构与工作流在代码中的具体展现

老师可能问法：你们这张 Agent 架构图具体落在哪些代码里？如果让我按顺序走一遍整体架构和工作流，你应该展示哪些文件、哪些行？

代码讲解顺序：建议按“UI 入口 -> 会话创建 -> 依赖注入架构 -> Agent A 编排 -> B/C/D 子 Agent -> 工具与 RAG -> 回复反馈 -> Memory 与持久化 -> 首轮与追问流程”来讲，正好对应 `agent_architecture_diagram.png` 和 `reasoning_flow_diagram.png`。

### 11.1 UI 入口：WPF 只负责触发和展示，不直接写智能逻辑

代码位置：

- `InvestAgent.Desktop/ViewModels/MainViewModel.cs:20`：`MainViewModel` 实现 `IAnalysisStreamingObserver`，负责接收 Agent 步骤和状态更新。
- `InvestAgent.Desktop/ViewModels/MainViewModel.cs:97-106`：构造函数注入 `IAgentSessionFactory`、`ISessionAnalysisOrchestrator`、`IStockDataService`、`IAnalysisHistoryRepository`。
- `InvestAgent.Desktop/ViewModels/MainViewModel.cs:220-252`：首轮分析入口 `RunAsync`，创建会话后调用编排器。
- `InvestAgent.Desktop/ViewModels/MainViewModel.cs:267-281`：多轮追问入口 `SendChatAsync`，继续使用同一会话上下文。

关键代码片段：

```csharp
private readonly IAgentSessionFactory _sessionFactory;
private readonly ISessionAnalysisOrchestrator _orchestrator;
private readonly IAnalysisHistoryRepository _historyRepository;

_currentSessionContext = _sessionFactory.Create(input);
var steps = await _orchestrator.RunInitialAnalysisAsync(_currentSessionContext, input, this);

var steps = await _orchestrator.HandleChatAsync(_currentSessionContext, UserMessage, this);
```

解释：这说明界面层不是直接拼 prompt 调 LLM，而是把“开始分析”和“追问”交给 Agent 编排层。UI 还实现观察者接口，所以 Thought、Action、Observation、Response 可以实时显示在界面步骤面板中。

### 11.2 会话上下文：每一次分析都有独立 Memory、状态和工作流记录

代码位置：

- `InvestAgent.Core/Agent/AgentSessionFactory.cs:31-43`：新建 `AgentSessionContext`。
- `InvestAgent.Core/Agent/AgentSessionFactory.cs:46-68`：从历史记录恢复会话、消息和工作流。
- `InvestAgent.Core/Agent/AgentSessionContext.cs:17-37`：上下文保存 `ConversationMemory`、`SessionState`、`Messages`、`WorkflowRuns`。

关键代码片段：

```csharp
public AgentSessionContext Create(string symbol, string stockName = "", long sessionId = 0)
{
    var state = new StockAnalysisSessionState
    {
        SessionId = sessionId,
        Symbol = symbol,
        StockName = stockName,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    };
    return new AgentSessionContext(CreateMemory(), state);
}
```

解释：架构图里的 “Memory 与持久化” 不是抽象口号。运行时每个会话都有 `Memory` 保存对话上下文，有 `State` 保存主营业务、K 线、新闻、财务分析，有 `WorkflowRuns` 保存本轮 Agent 执行轨迹。

### 11.3 依赖注入：把工具、Memory、Semantic Kernel、B/C/D 子 Agent 装配成系统

代码位置：

- `InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs:55-63`：注册股票、新闻、财务、RAG 等数据服务。
- `InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs:74-81`：注册 `WorkingMemory` 和 `ConversationMemory`。
- `InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs:83-119`：注册 5 个 Semantic Kernel 插件：`StockPrice`、`FinancialReport`、`MarketNews`、`TechnicalAnalysis`、`LocalKnowledge`。
- `InvestAgent.Core/Extensions/ServiceCollectionExtensions.cs:129-140`：注册 `AgentPromptRunner`、`AgentSessionFactory`、B/C/D 子 Agent、Agent A 编排器和 `InvestAgentLoop`。

关键代码片段：

```csharp
services.AddSingleton<StockPricePlugin>();
services.AddSingleton<FinancialReportPlugin>();
services.AddSingleton<MarketNewsPlugin>();
services.AddSingleton<TechnicalAnalysisPlugin>();
services.AddSingleton<LocalKnowledgePlugin>();

builder.Plugins.AddFromObject(sp.GetRequiredService<StockPricePlugin>(), "StockPrice");
builder.Plugins.AddFromObject(sp.GetRequiredService<FinancialReportPlugin>(), "FinancialReport");
builder.Plugins.AddFromObject(sp.GetRequiredService<MarketNewsPlugin>(), "MarketNews");
builder.Plugins.AddFromObject(sp.GetRequiredService<TechnicalAnalysisPlugin>(), "TechnicalAnalysis");
builder.Plugins.AddFromObject(sp.GetRequiredService<LocalKnowledgePlugin>(), "LocalKnowledge");

services.AddSingleton<ISubAgentService, AgentBService>();
services.AddSingleton<ISubAgentService, AgentCService>();
services.AddSingleton<ISubAgentService, AgentDService>();
services.AddSingleton<ISessionAnalysisOrchestrator, SessionAnalysisOrchestrator>();
```

解释：这段代码对应架构图中的“工具与数据层”和 “Agent B/C/D”。插件提供可被 SK 调用的工具能力，B/C/D 是更高层的专业分析 Agent，Agent A 负责把它们组织起来。

### 11.4 Agent A 编排层：集中做任务规划、派单、状态合成和最终回复

代码位置：

- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:9-28`：类注释说明它是多 Agent 工作流指挥者；构造函数把 B/C/D 子 Agent 放进字典。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:30-67`：首轮分析 `RunInitialAnalysisAsync`。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:69-136`：追问分析 `HandleChatAsync`。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:182-236`：`BuildDispatchPlanAsync` 根据用户问题生成派单计划。

关键代码片段：

```csharp
_subAgents = subAgents.ToDictionary(x => x.AgentName.Replace("Agent ", ""), x => x);

var tasks = new List<SubAgentTask>
{
    new() { Agent = "B", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始K线分析..." },
    new() { Agent = "C", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始新闻分析..." },
    new() { Agent = "D", IsInitialAnalysis = true, Instruction = $"对 {context.State.Symbol} 做初始财务分析..." }
};

steps.AddRange(await ExecuteInitialTasksInParallelAsync(context, tasks, turnIndex, observer));
await FinalizeAssistantResponseAsync(context, targetInput, tasks, steps, "初始分析", turnIndex, true, observer);
```

解释：Agent A 不直接完成所有分析，而是先识别任务，再把 K 线、新闻、财务分别派给 B/C/D。首轮分析固定覆盖四个角度；追问时通过 `BuildDispatchPlanAsync` 只派发相关子任务。

### 11.5 B/C/D 子 Agent：专业分工对应架构图中的三个分析节点

代码位置：

- `InvestAgent.Core/Agent/AgentBService.cs:12-23`：Agent B 定义为 K 线、缠论 RAG、历史走势 RAG 子 Agent。
- `InvestAgent.Core/Agent/AgentBService.cs:37-166`：拉取日线/月线、检索缠论知识与历史相似走势、生成技术分析并回写状态。
- `InvestAgent.Core/Agent/AgentCService.cs:13-18`：Agent C 定义为新闻子 Agent。
- `InvestAgent.Core/Agent/AgentCService.cs:26-144`：筛选公司新闻、行业新闻、正负面事件并生成新闻分析。
- `InvestAgent.Core/Agent/AgentDService.cs:11-16`：Agent D 定义为财务子 Agent。
- `InvestAgent.Core/Agent/AgentDService.cs:24-84`：拉取关键财务指标并生成财务分析。

关键代码片段：

```csharp
public class AgentBService : ISubAgentService
{
    public string AgentName => "Agent B";
    // K线 + 缠论RAG + 历史走势RAG
}

public class AgentCService : ISubAgentService
{
    public string AgentName => "Agent C";
    // 新闻动态与情绪归因
}

public class AgentDService : ISubAgentService
{
    public string AgentName => "Agent D";
    // 财务报表与关键指标
}
```

解释：三个类都实现 `ISubAgentService`，因此它们是统一接口下的专业 Agent。Agent A 只依赖接口和 `AgentName` 派单，后续如果增加“估值 Agent”或“资金流 Agent”，也可以按同样方式扩展。

### 11.6 工具与 RAG：B/C/D 通过服务层获得结构化数据，再交给 LLM 组织答案

代码位置：

- `InvestAgent.Core/Agent/AgentBService.cs:55-71`：获取日线和月线 K 线数据。
- `InvestAgent.Core/Agent/AgentBService.cs:86-95`：调用 `IHistoricalPatternService.SearchSimilarPatterns` 做历史走势 RAG。
- `InvestAgent.Core/Agent/AgentBService.cs:99-124`：调用 `ILocalKnowledgeService.Search` 和 `SearchChanImages` 做缠论 RAG。
- `InvestAgent.Core/Agent/AgentBService.cs:150-158`：把图文提示交给 `AgentPromptRunner` 流式生成。
- `InvestAgent.Core/Agent/AgentCService.cs:76-98`：把新闻数据写入状态。
- `InvestAgent.Core/Agent/AgentDService.cs:53-59`：把财务历史数据写入状态。

关键代码片段：

```csharp
historicalPatterns = _historicalPatternService.SearchSimilarPatterns(
    context.State.Symbol, daily, task.Instruction, 8);

chanSections = _localKnowledgeService.Search("chan", task.Instruction, 3);
chanImages = _localKnowledgeService.SearchChanImages(task.Instruction, MaxChanImagesPerPrompt);
```

解释：工具层不是只给 LLM 一个自然语言问题，而是先取结构化股票数据、新闻数据、财务数据，再把检索到的缠论文本、图例和历史相似走势作为证据交给模型生成分析。这样能体现 RAG、工具调用和领域 Agent 的结合。

### 11.7 回复反馈、Memory 与持久化：子 Agent 结果先回写状态，再由 Agent A 汇总

代码位置：

- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:238-274`：追问时按派单计划执行子 Agent，并把 `StatePatch` 合并回会话状态。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:276-295`：首轮分析并行执行 B/C/D。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:335-355`：`FinalizeAssistantResponseAsync` 准备最终回答。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:461-488`：`BuildFinalResponse` 把主营业务、K 线、新闻、财务、风险提示组织成最终报告。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:823-846`：`AddConversationAsync` 写入对话消息。
- `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:862-870`：`AddWorkflowRun` 保存每轮工作流记录。
- `InvestAgent.Desktop/ViewModels/MainViewModel.cs:346-372`：`SaveCurrentSessionAsync` 把状态、消息、工作流保存到 SQLite。
- `InvestAgent.Desktop/Services/AnalysisHistoryRepository.cs:34-80`：`SaveSessionAsync` 保存会话、消息和工作流。
- `InvestAgent.Desktop/Services/AnalysisHistoryRepository.cs:179-206`：创建 `analysis_sessions`、`analysis_messages`、`analysis_workflow_runs`、`analysis_session_snapshots` 表。

关键代码片段：

```csharp
var result = await service.ExecuteAsync(context, task, observer, turnIndex);
result.StatePatch?.ApplyTo(context.State);

context.WorkflowRuns.Add(new WorkflowRunRecord
{
    AgentName = agentName,
    StepsJson = JsonSerializer.Serialize(steps, JsonOptions)
});

var sessionId = await _historyRepository.SaveSessionAsync(persisted);
```

解释：架构图里“工具层 -> 回复反馈 -> Memory 与持久化 -> Agent A”对应这里的状态 Patch、最终回复生成、对话记录和工作流记录。Memory/持久化给下一轮 Agent A 提供上下文，所以多轮追问能继承前一轮结果。

### 11.8 工作流按顺序怎么讲

首轮分析代码路线：

1. `InvestAgent.Desktop/ViewModels/MainViewModel.cs:220-252`：用户输入股票，UI 创建会话并调用 `RunInitialAnalysisAsync`。
2. `InvestAgent.Core/Agent/AgentSessionFactory.cs:31-43`：创建 `AgentSessionContext`，初始化 Memory 和 SessionState。
3. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:30-67`：Agent A 写入用户消息，生成 Thought/Action，先获取主营业务，再构造 B/C/D 三个任务。
4. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:276-295`：`ExecuteInitialTasksInParallelAsync` 用 `Task.WhenAll` 并行执行三个子 Agent。
5. `InvestAgent.Core/Agent/AgentBService.cs:37-166`：Agent B 生成 K 线、缠论 RAG、历史走势 RAG 分析。
6. `InvestAgent.Core/Agent/AgentCService.cs:26-144`：Agent C 生成新闻动态分析。
7. `InvestAgent.Core/Agent/AgentDService.cs:24-84`：Agent D 生成财务报表分析。
8. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:335-488`：Agent A 汇总状态，生成最终 Response。
9. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:823-870` 与 `InvestAgent.Desktop/ViewModels/MainViewModel.cs:346-372`：写入 Memory、工作流记录和 SQLite 历史。

多轮追问代码路线：

1. `InvestAgent.Desktop/ViewModels/MainViewModel.cs:267-281`：用户在同一会话里发送追问，调用 `HandleChatAsync`。
2. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:69-136`：Agent A 先判断是否切换股票、写入用户消息，再进入追问处理。
3. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:182-236`：`BuildDispatchPlanAsync` 根据问题关键词和 LLM 计划决定派给 B、C、D 中哪些 Agent。
4. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:238-274`：`ExecuteTasksAsync` 执行相关子 Agent 并合并状态。
5. `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:491-546`：`BuildScopedFollowUpResponse` 生成本轮追问的聚焦回答。
6. `InvestAgent.Core/Memory/ConversationMemory.cs:43-59` 与 `InvestAgent.Core/Agent/SessionAnalysisOrchestrator.cs:823-846`：本轮用户问题和助手回复继续写入 Memory，供下一轮使用。

答辩时可以这样总结：本项目的 Agent 架构体现在三层代码中。第一层是 `MainViewModel`，负责用户交互和可视化步骤；第二层是 `SessionAnalysisOrchestrator`，也就是 Agent A，负责 Thought、派单、Observation 汇总和 Response；第三层是 B/C/D 子 Agent 与工具/RAG 服务，分别处理 K 线、新闻、财务。工作流不是一次性问答，而是“用户输入 -> 会话和 Memory -> Agent A 规划 -> B/C/D 工具执行 -> Observation 写回状态 -> Agent A 汇总回复 -> Memory 与 SQLite 持久化 -> 下一轮继续使用上下文”的闭环。

## 5 分钟展示口播节奏

- 第 1 页，20 秒：说明项目名称、成员和一句话定位。
- 第 2 页，45 秒：对照课程要求说明五个核心要素和加分项。
- 第 3 页，60 秒：讲架构图，重点解释 Agent A、B/C/D、工具层、回复反馈和 Memory。
- 第 4 页，60 秒：讲推理流程，解释 Thought、Action、Observation、Response。
- 第 5 页，80 秒：讲核心功能闭环：主营业务、K 线、财务、新闻、RAG、多轮追问。
- 第 6 页，15 秒：感谢老师指导。

## 实机演示顺序

1. 输入 `600519` 或现场网络较稳定的股票代码。
2. 展示主营业务、K 线、新闻、财务四个区域。
3. 展示步骤面板中的 Thought / Action / Observation。
4. 追问“用缠论分析最近走势”或“只看消极新闻”。
5. 展示历史记录回放。

