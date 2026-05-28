using System.ComponentModel;
using System.Text.Json;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

public class StockPricePlugin
{
    private readonly IStockDataService _stockService;
    private readonly IWorkingMemory _workingMemory;
    private readonly ILogger<StockPricePlugin> _logger;

    public StockPricePlugin(IStockDataService stockService, IWorkingMemory workingMemory, ILogger<StockPricePlugin> logger)
    {
        _stockService = stockService;
        _workingMemory = workingMemory;
        _logger = logger;
    }

    [KernelFunction("get_current_price")]
    [Description("获取指定股票的实时行情数据，包含最新价、涨跌幅、成交量、换手率等。参数symbol为股票代码（如600519、AAPL）。")]
    [return: Description("股票实时行情JSON")]
    public async Task<string> GetCurrentPriceAsync(
        [Description("股票代码，例如600519或AAPL")] string symbol)
    {
        _logger.LogInformation("调用 GetCurrentPrice: {Symbol}", symbol);
        var cacheKey = $"quote:{symbol}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetCurrentPriceAsync(symbol),
            TimeSpan.FromSeconds(60));

        if (result == null)
            return $"未找到股票 {symbol} 的行情数据。";

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_historical_prices")]
    [Description("获取指定股票近N日的日K线历史数据，包含每日的开盘价、最高价、最低价、收盘价、成交量。用于技术分析计算。")]
    [return: Description("历史K线数据JSON数组")]
    public async Task<string> GetHistoricalPricesAsync(
        [Description("股票代码，例如600519或AAPL")] string symbol,
        [Description("天数，默认30天")] int days = 30)
    {
        _logger.LogInformation("调用 GetHistoricalPrices: {Symbol}, {Days}天", symbol, days);
        var cacheKey = $"kline:{symbol}:{days}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetHistoricalPricesAsync(symbol, days),
            TimeSpan.FromMinutes(5));

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("search_stock")]
    [Description("根据公司名称或部分股票代码搜索对应的股票代码和名称。当用户输入的是公司名而非代码时需要先调用此函数。")]
    [return: Description("搜索结果JSON数组")]
    public async Task<string> SearchStockAsync(
        [Description("搜索关键词，如公司名或代码")] string keyword)
    {
        _logger.LogInformation("调用 SearchStock: {Keyword}", keyword);
        var result = await _stockService.SearchStockAsync(keyword);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_monthly_kline")]
    [Description("获取指定股票的月K线历史数据，用于分析中长期趋势。返回数年的月度开盘价、最高价、最低价、收盘价、成交量。适合用来判断股票的长期走势和市场周期。")]
    [return: Description("月K线数据JSON数组")]
    public async Task<string> GetMonthlyKLineAsync(
        [Description("股票代码，例如600519")] string symbol,
        [Description("月数，默认36个月（3年）")] int months = 36)
    {
        _logger.LogInformation("调用 GetMonthlyKLine: {Symbol}, {Months}个月", symbol, months);
        var cacheKey = $"monthly-kline:{symbol}:{months}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetMonthlyKLineAsync(symbol, months),
            TimeSpan.FromMinutes(10));

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_capital_flow")]
    [Description("获取指定股票近期的资金流向数据，包含主力资金、超大单、大单、中单、小单的每日净流入金额（单位：元）。正值表示资金净流入，负值表示资金净流出。用于判断主力资金的动向和市场资金态度。")]
    [return: Description("资金流向数据JSON数组")]
    public async Task<string> GetCapitalFlowAsync(
        [Description("股票代码，例如600519")] string symbol,
        [Description("天数，默认20天")] int days = 20)
    {
        _logger.LogInformation("调用 GetCapitalFlow: {Symbol}, {Days}天", symbol, days);
        var cacheKey = $"capitalflow:{symbol}:{days}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetCapitalFlowAsync(symbol, days),
            TimeSpan.FromMinutes(5));

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
