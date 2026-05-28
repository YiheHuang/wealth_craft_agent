using InvestAgent.Core.Models;

namespace InvestAgent.Core.Services;

public interface IStockDataService
{
    Task<StockQuote?> GetCurrentPriceAsync(string symbol);
    Task<List<StockKLine>> GetHistoricalPricesAsync(string symbol, int days = 30);
    Task<List<StockKLine>> GetMonthlyKLineAsync(string symbol, int months = 36);
    Task<List<StockQuote>> SearchStockAsync(string keyword);
    Task<KeyMetrics?> GetKeyMetricsAsync(string symbol);
    Task<List<KeyMetrics>> GetKeyMetricsHistoryAsync(string symbol, int maxReports = 4);
    Task<string> GetMainBusinessAsync(string symbol);
    Task<List<NewsItem>> GetLatestNewsAsync(string symbol, int count = 5);
    Task<List<CapitalFlowItem>> GetCapitalFlowAsync(string symbol, int days = 20);
    string SourceName { get; }
}
