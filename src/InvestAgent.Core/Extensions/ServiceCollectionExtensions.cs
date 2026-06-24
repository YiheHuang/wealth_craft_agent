using InvestAgent.Core.Agent;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Plugins;
using InvestAgent.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace InvestAgent.Core.Extensions;

/// <summary>
/// InvestAgent 核心服务的 DI 注册扩展方法。
/// 集中管理所有服务、插件、Agent 和 Semantic Kernel 的依赖注入配置。
/// 通过 <c>services.AddInvestAgent(options)</c> 一键完成全部注册。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 向 DI 容器注册 InvestAgent 的全部核心服务。
    /// 包括：数据服务、HTTP 缓存、记忆管理、Semantic Kernel 插件、
    /// Agent 编排器和子 Agent 服务。
    /// </summary>
    /// <param name="services">DI 服务集合</param>
    /// <param name="options">全局配置选项</param>
    /// <returns>服务集合（支持链式调用）</returns>
    public static IServiceCollection AddInvestAgent(this IServiceCollection services, AgentOptions options)
    {
        // ── HTTP 缓存 ────────────────────────────────────
        services.AddSingleton<IHttpCache>(sp => new FileHttpCache(options));

        // ── HTTP 客户端 ───────────────────────────────────
        // 配置代理、超时和 User-Agent
        services.AddSingleton(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ResponseDrainTimeout = TimeSpan.FromSeconds(30)
            };
            if (!string.IsNullOrWhiteSpace(options.ProxyUrl))
                handler.Proxy = new System.Net.WebProxy(options.ProxyUrl);

            return new HttpClient(handler)
            {
                DefaultRequestHeaders =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" },
                    { "Accept", "application/json" }
                }
            };
        });

        // ── 数据服务（按 DataSource 配置切换）─────────────
        services.AddSingleton<EastMoneyStockService>();
        services.AddSingleton<YahooFinanceStockService>();
        services.AddSingleton<AlphaVantageNewsService>();
        services.AddSingleton<FinnhubCapitalFlowService>();
        services.AddSingleton<CompositeStockDataService>();
        services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();
        services.AddSingleton<ILocalKnowledgeService, LocalKnowledgeService>();
        services.AddSingleton<IHistoricalPatternService, HistoricalPatternService>();

        // 根据配置选择数据源实现
        services.AddSingleton<IStockDataService>(sp => options.DataSource switch
        {
            "yahoo" => sp.GetRequiredService<YahooFinanceStockService>(),
            "eastmoney" => sp.GetRequiredService<EastMoneyStockService>(),
            "composite" => sp.GetRequiredService<CompositeStockDataService>(),
            _ => sp.GetRequiredService<CompositeStockDataService>()
        });

        // ── 记忆管理 ────────────────────────────────────
        services.AddSingleton<IWorkingMemory, WorkingMemory>();
        services.AddSingleton<IConversationMemory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConversationMemory>>();
            var systemPrompt = sp.GetRequiredService<ISystemPromptProvider>().GetDefaultSystemPrompt();
            return new ConversationMemory(systemPrompt, options.MaxConversationTurns, logger);
        });

        // ── 插件（Semantic Kernel Functions）──────────────
        services.AddSingleton<StockPricePlugin>();
        services.AddSingleton<FinancialReportPlugin>();
        services.AddSingleton<MarketNewsPlugin>();
        services.AddSingleton<TechnicalAnalysisPlugin>();
        services.AddSingleton<LocalKnowledgePlugin>();

        // ── Semantic Kernel ───────────────────────────────
        services.AddSingleton(sp =>
        {
            var builder = Kernel.CreateBuilder();

            // LLM 专用 HTTP 客户端（更长超时）
            var llmHandler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ResponseDrainTimeout = TimeSpan.FromSeconds(120)
            };
            if (!string.IsNullOrWhiteSpace(options.ProxyUrl))
                llmHandler.Proxy = new System.Net.WebProxy(options.ProxyUrl);

            var httpClient = new HttpClient(llmHandler);

#pragma warning disable SKEXP0010
            builder.AddOpenAIChatCompletion(
                modelId: options.ModelId,
                apiKey: options.ApiKey,
                httpClient: httpClient,
                endpoint: new Uri(options.Endpoint));
#pragma warning restore SKEXP0010

            // 将所有插件注册到 Kernel（5 个插件）
            builder.Plugins.AddFromObject(sp.GetRequiredService<StockPricePlugin>(), "StockPrice");
            builder.Plugins.AddFromObject(sp.GetRequiredService<FinancialReportPlugin>(), "FinancialReport");
            builder.Plugins.AddFromObject(sp.GetRequiredService<MarketNewsPlugin>(), "MarketNews");
            builder.Plugins.AddFromObject(sp.GetRequiredService<TechnicalAnalysisPlugin>(), "TechnicalAnalysis");
            builder.Plugins.AddFromObject(sp.GetRequiredService<LocalKnowledgePlugin>(), "LocalKnowledge");

            return builder.Build();
        });

        // ── ChatCompletion 服务 ───────────────────────────
        services.AddSingleton(sp =>
            sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());

        // ── Agent 编排层 ──────────────────────────────────
        services.AddSingleton<IAgentPromptRunner, AgentPromptRunner>();
        services.AddSingleton<IAgentSessionFactory, AgentSessionFactory>();

        // 注册子 Agent 服务（多实现，以 IEnumerable 方式注入）
        services.AddSingleton<ISubAgentService, AgentBService>();
        services.AddSingleton<ISubAgentService, AgentCService>();
        services.AddSingleton<ISubAgentService, AgentDService>();

        services.AddSingleton<ISessionAnalysisOrchestrator, SessionAnalysisOrchestrator>();

        // ── Agent 主循环 ──────────────────────────────────
        services.AddSingleton<InvestAgentLoop>();

        return services;
    }
}
