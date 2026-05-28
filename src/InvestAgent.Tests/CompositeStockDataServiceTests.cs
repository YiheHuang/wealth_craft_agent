using InvestAgent.Core.Configuration;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvestAgent.Tests;

public class CompositeStockDataServiceTests
{
    [Fact]
    public async Task Composite_News_Should_Return_DataGap_When_AlphaKey_Missing()
    {
        var options = new AgentOptions
        {
            AlphaVantageEnabled = true,
            AlphaVantageApiKey = "",
            FinnhubEnabled = true,
            FinnhubApiKey = ""
        };
        var cache = new FileHttpCache(options);
        var http = new HttpClient();
        var yahoo = new YahooFinanceStockService(http, cache, NullLogger<YahooFinanceStockService>.Instance);
        var news = new AlphaVantageNewsService(http, cache, options, NullLogger<AlphaVantageNewsService>.Instance);
        var flow = new FinnhubCapitalFlowService(http, cache, options, NullLogger<FinnhubCapitalFlowService>.Instance);
        var eastMoney = new EastMoneyStockService(http, cache, NullLogger<EastMoneyStockService>.Instance);
        var composite = new CompositeStockDataService(yahoo, news, flow, eastMoney);

        var result = await composite.GetLatestNewsAsync("AAPL", 3);
        Assert.NotEmpty(result);
        Assert.False(result[0].IsDataAvailable);
    }

    [Fact]
    public async Task Composite_Flow_Should_Return_DataGap_When_FinnhubKey_Missing()
    {
        var options = new AgentOptions
        {
            AlphaVantageEnabled = true,
            AlphaVantageApiKey = "",
            FinnhubEnabled = true,
            FinnhubApiKey = ""
        };
        var cache = new FileHttpCache(options);
        var http = new HttpClient();
        var yahoo = new YahooFinanceStockService(http, cache, NullLogger<YahooFinanceStockService>.Instance);
        var news = new AlphaVantageNewsService(http, cache, options, NullLogger<AlphaVantageNewsService>.Instance);
        var flow = new FinnhubCapitalFlowService(http, cache, options, NullLogger<FinnhubCapitalFlowService>.Instance);
        var eastMoney = new EastMoneyStockService(http, cache, NullLogger<EastMoneyStockService>.Instance);
        var composite = new CompositeStockDataService(yahoo, news, flow, eastMoney);

        var result = await composite.GetCapitalFlowAsync("AAPL", 10);
        Assert.NotEmpty(result);
        Assert.False(result[0].IsDataAvailable);
    }
}
