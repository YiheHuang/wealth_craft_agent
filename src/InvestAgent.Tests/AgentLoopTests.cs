using InvestAgent.Core.Agent;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Tests;

/// <summary>
/// Agent 循环与 DI 容器的集成测试。
/// 验证所有核心服务能否正确解析、插件是否注册完整、函数是否可发现。
/// </summary>
public class AgentLoopTests
{
    /// <summary>创建测试用的 DI 容器——最小化配置，禁用日志</summary>
    private IServiceProvider CreateProvider()
    {
        var options = new AgentOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://yunwu.ai/v1",
            ModelId = "gpt-4o-mini",
            MaxSteps = 2 // 限制步数以加速测试
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.None));
        services.AddSingleton(options);
        services.AddInvestAgent(options);
        return services.BuildServiceProvider();
    }

    /// <summary>验证 DI 容器能够成功构建并解析核心类型</summary>
    [Fact]
    public async Task DI_Container_Builds_Successfully()
    {
        var provider = CreateProvider();

        // 验证 InvestAgentLoop 可解析
        var agent = provider.GetRequiredService<InvestAgentLoop>();
        Assert.NotNull(agent);

        // 验证 Kernel 可解析且插件数量 >= 4（StockPrice, FinancialReport, MarketNews, TechnicalAnalysis）
        var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
        Assert.NotNull(kernel);
        Assert.True(kernel.Plugins.Count >= 4, $"Expected >= 4 plugins, got {kernel.Plugins.Count}");
    }

    /// <summary>验证四个核心插件以正确的名称注册</summary>
    [Fact]
    public void Four_Plugins_Are_Registered_With_Correct_Names()
    {
        var provider = CreateProvider();
        var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
        var pluginNames = kernel.Plugins.Select(p => p.Name).ToList();

        Assert.Contains("StockPrice", pluginNames);
        Assert.Contains("FinancialReport", pluginNames);
        Assert.Contains("MarketNews", pluginNames);
        Assert.Contains("TechnicalAnalysis", pluginNames);
    }

    /// <summary>验证所有 13 个 KernelFunction 均正确注册</summary>
    [Fact]
    public void All_Fifteen_Functions_Are_Registered()
    {
        var provider = CreateProvider();
        var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();

        // StockPrice 插件——5 个函数
        var stockPlugin = kernel.Plugins["StockPrice"];
        Assert.Contains(stockPlugin, f => f.Name == "get_current_price");
        Assert.Contains(stockPlugin, f => f.Name == "get_historical_prices");
        Assert.Contains(stockPlugin, f => f.Name == "search_stock");
        Assert.Contains(stockPlugin, f => f.Name == "get_monthly_kline");
        Assert.Contains(stockPlugin, f => f.Name == "get_capital_flow");

        // FinancialReport 插件——2 个函数
        var finPlugin = kernel.Plugins["FinancialReport"];
        Assert.Contains(finPlugin, f => f.Name == "get_key_metrics");
        Assert.Contains(finPlugin, f => f.Name == "get_profit_analysis");

        // MarketNews 插件——2 个函数
        var newsPlugin = kernel.Plugins["MarketNews"];
        Assert.Contains(newsPlugin, f => f.Name == "get_latest_news");
        Assert.Contains(newsPlugin, f => f.Name == "get_market_sentiment");

        // TechnicalAnalysis 插件——4 个函数
        var techPlugin = kernel.Plugins["TechnicalAnalysis"];
        Assert.Contains(techPlugin, f => f.Name == "calculate_ma");
        Assert.Contains(techPlugin, f => f.Name == "calculate_rsi");
        Assert.Contains(techPlugin, f => f.Name == "calculate_macd");
        Assert.Contains(techPlugin, f => f.Name == "generate_trading_signal");
    }
}
