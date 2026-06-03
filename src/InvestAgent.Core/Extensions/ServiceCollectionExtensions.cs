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

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInvestAgent(this IServiceCollection services, AgentOptions options)
    {
        // 数据服务 — 可切换数据源
        services.AddSingleton<IHttpCache>(sp => new FileHttpCache(options));
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
        services.AddSingleton<EastMoneyStockService>();
        services.AddSingleton<YahooFinanceStockService>();
        services.AddSingleton<AlphaVantageNewsService>();
        services.AddSingleton<FinnhubCapitalFlowService>();
        services.AddSingleton<CompositeStockDataService>();
        services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();
        services.AddSingleton<ILocalKnowledgeService, LocalKnowledgeService>();
        services.AddSingleton<IStockDataService>(sp => options.DataSource switch
        {
            "yahoo" => sp.GetRequiredService<YahooFinanceStockService>(),
            "eastmoney" => sp.GetRequiredService<EastMoneyStockService>(),
            "composite" => sp.GetRequiredService<CompositeStockDataService>(),
            _ => sp.GetRequiredService<CompositeStockDataService>()
        });

        // 记忆
        services.AddSingleton<IWorkingMemory, WorkingMemory>();
        services.AddSingleton<IConversationMemory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConversationMemory>>();
            var systemPrompt = sp.GetRequiredService<ISystemPromptProvider>().GetDefaultSystemPrompt();
            return new ConversationMemory(systemPrompt, options.MaxConversationTurns, logger);
        });

        // 插件
        services.AddSingleton<StockPricePlugin>();
        services.AddSingleton<FinancialReportPlugin>();
        services.AddSingleton<MarketNewsPlugin>();
        services.AddSingleton<TechnicalAnalysisPlugin>();
        services.AddSingleton<LocalKnowledgePlugin>();

        // Semantic Kernel
        services.AddSingleton(sp =>
        {
            var builder = Kernel.CreateBuilder();

            var httpClient = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ResponseDrainTimeout = TimeSpan.FromSeconds(120)
            });

#pragma warning disable SKEXP0010
            builder.AddOpenAIChatCompletion(
                modelId: options.ModelId,
                apiKey: options.ApiKey,
                httpClient: httpClient,
                endpoint: new Uri(options.Endpoint));
#pragma warning restore SKEXP0010

            // 注册所有插件
            builder.Plugins.AddFromObject(sp.GetRequiredService<StockPricePlugin>(), "StockPrice");
            builder.Plugins.AddFromObject(sp.GetRequiredService<FinancialReportPlugin>(), "FinancialReport");
            builder.Plugins.AddFromObject(sp.GetRequiredService<MarketNewsPlugin>(), "MarketNews");
            builder.Plugins.AddFromObject(sp.GetRequiredService<TechnicalAnalysisPlugin>(), "TechnicalAnalysis");
            builder.Plugins.AddFromObject(sp.GetRequiredService<LocalKnowledgePlugin>(), "LocalKnowledge");

            return builder.Build();
        });

        // ChatCompletion 服务
        services.AddSingleton(sp =>
            sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());

        services.AddSingleton<IAgentPromptRunner, AgentPromptRunner>();
        services.AddSingleton<IAgentSessionFactory, AgentSessionFactory>();
        services.AddSingleton<ISubAgentService, AgentBService>();
        services.AddSingleton<ISubAgentService, AgentCService>();
        services.AddSingleton<ISubAgentService, AgentDService>();
        services.AddSingleton<ISessionAnalysisOrchestrator, SessionAnalysisOrchestrator>();

        // Agent Loop
        services.AddSingleton<InvestAgentLoop>();

        return services;
    }
}
