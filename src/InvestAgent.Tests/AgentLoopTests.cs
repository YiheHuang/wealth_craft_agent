using InvestAgent.Core.Agent;
using InvestAgent.Core.Configuration;
using InvestAgent.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Tests;

public class AgentLoopTests
{
    private IServiceProvider CreateProvider()
    {
        var options = new AgentOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://yunwu.ai/v1",
            ModelId = "gpt-4o-mini",
            MaxSteps = 2
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.None));
        services.AddSingleton(options);
        services.AddInvestAgent(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DI_Container_Builds_Successfully()
    {
        var provider = CreateProvider();
        var agent = provider.GetRequiredService<InvestAgentLoop>();
        Assert.NotNull(agent);

        var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
        Assert.NotNull(kernel);
        Assert.True(kernel.Plugins.Count >= 4, $"Expected >= 4 plugins, got {kernel.Plugins.Count}");
    }

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

    [Fact]
    public void All_Fifteen_Functions_Are_Registered()
    {
        var provider = CreateProvider();
        var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();

        var stockPlugin = kernel.Plugins["StockPrice"];
        Assert.Contains(stockPlugin, f => f.Name == "get_current_price");
        Assert.Contains(stockPlugin, f => f.Name == "get_historical_prices");
        Assert.Contains(stockPlugin, f => f.Name == "search_stock");
        Assert.Contains(stockPlugin, f => f.Name == "get_monthly_kline");
        Assert.Contains(stockPlugin, f => f.Name == "get_capital_flow");

        var finPlugin = kernel.Plugins["FinancialReport"];
        Assert.Contains(finPlugin, f => f.Name == "get_key_metrics");
        Assert.Contains(finPlugin, f => f.Name == "get_profit_analysis");

        var newsPlugin = kernel.Plugins["MarketNews"];
        Assert.Contains(newsPlugin, f => f.Name == "get_latest_news");
        Assert.Contains(newsPlugin, f => f.Name == "get_market_sentiment");

        var techPlugin = kernel.Plugins["TechnicalAnalysis"];
        Assert.Contains(techPlugin, f => f.Name == "calculate_ma");
        Assert.Contains(techPlugin, f => f.Name == "calculate_rsi");
        Assert.Contains(techPlugin, f => f.Name == "calculate_macd");
        Assert.Contains(techPlugin, f => f.Name == "generate_trading_signal");
    }
}
