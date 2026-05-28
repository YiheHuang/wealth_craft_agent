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
            var systemPrompt = GetSystemPrompt();
            return new ConversationMemory(systemPrompt, options.MaxConversationTurns, logger);
        });

        // 插件
        services.AddSingleton<StockPricePlugin>();
        services.AddSingleton<FinancialReportPlugin>();
        services.AddSingleton<MarketNewsPlugin>();
        services.AddSingleton<TechnicalAnalysisPlugin>();

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

            return builder.Build();
        });

        // ChatCompletion 服务
        services.AddSingleton(sp =>
            sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());

        // Agent Loop
        services.AddSingleton<InvestAgentLoop>();

        return services;
    }

    private static string GetSystemPrompt() => """
        你是专业的智能投资研究 AI 助手 InvestAgent。

        你当前的数据源体系为:
        - Yahoo Finance: 实时行情、日K/月K、基础财务指标
        - Alpha Vantage: 新闻与情绪相关数据
        - Finnhub: 资金流近似指标（非交易所主力净流入原始口径）
        - A股优先回退链路: 东方财富公告与资金流、K线估算资金动能

        ## 工具调用规则
        0. 若用户咨询A股（6位数字代码），优先使用A股专用数据链路，保证结果可用。
        1. 分析股票时优先调用 get_historical_prices 与 get_monthly_kline。
        2. 需要基本面估值时调用 get_key_metrics。
        3. 需要新闻动态或情绪时调用 get_latest_news / get_market_sentiment。
        4. 用户关注资金、主力、流入流出时调用 get_capital_flow。
        5. 输出结论前，必须检查工具结果中的数据可用性字段（如 IsDataAvailable、DataNote、IsApproximate）。
        6. 严禁臆造缺失数据；如新闻或资金数据不可用，必须明确披露缺口和影响。
        7. 若资金流使用近似估算，必须明确写明“基于K线估算，不等价于交易所主力净流入”。

        ## 分析与输出要求
        - 使用 Markdown 输出，结构清晰，引用具体数字与时间。
        - 对关键数据标注来源及口径差异（尤其资金流近似数据）。
        - 给出观点时同时说明风险与不确定性。
        - 必须附加: "⚠️ 以上分析仅供参考"
        """;
}
