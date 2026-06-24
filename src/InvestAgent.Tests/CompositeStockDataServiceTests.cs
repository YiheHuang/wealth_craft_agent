using InvestAgent.Core.Configuration;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvestAgent.Tests;

/// <summary>
/// 组合数据服务的集成测试。
/// 验证 CompositeStockDataService 在 API Key 缺失时的降级行为——
/// 确保不会因为配置问题而抛出异常，而是优雅返回数据不可用标记。
/// </summary>
public class CompositeStockDataServiceTests
{
    /// <summary>
    /// 验证：Alpha Vantage Key 缺失时，新闻接口应返回不可用标记而非抛异常。
    /// </summary>
    [Fact]
    public async Task Composite_News_Should_Return_DataGap_When_AlphaKey_Missing()
    {
        var options = new AgentOptions
        {
            AlphaVantageEnabled = true,
            AlphaVantageApiKey = "", // Key 缺失
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
        // 应返回标记数据不可用的条目
        Assert.False(result[0].IsDataAvailable);
    }

    /// <summary>
    /// 验证：资金流功能已按产品要求移除，调用应返回空列表。
    /// </summary>
    [Fact]
    public async Task Composite_Flow_Should_Return_Empty_When_Flow_Feature_Removed()
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
        // 资金流功能已移除，应返回空列表
        Assert.Empty(result);
    }
}
