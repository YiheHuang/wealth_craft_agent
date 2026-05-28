using System.ComponentModel;
using System.Text.Json;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

public class FinancialReportPlugin
{
    private readonly IStockDataService _stockService;
    private readonly IWorkingMemory _workingMemory;
    private readonly ILogger<FinancialReportPlugin> _logger;

    public FinancialReportPlugin(IStockDataService stockService, IWorkingMemory workingMemory, ILogger<FinancialReportPlugin> logger)
    {
        _stockService = stockService;
        _workingMemory = workingMemory;
        _logger = logger;
    }

    [KernelFunction("get_key_metrics")]
    [Description("获取公司的核心财务指标，包含PE(市盈率)、PB(市净率)、ROE(净资产收益率)、ROA(总资产收益率)、毛利率、净利率、营收增长率、净利润增长率、资产负债率、总市值。用于估值分析和盈利能力评估。")]
    [return: Description("财务指标JSON")]
    public async Task<string> GetKeyMetricsAsync(
        [Description("股票代码，例如600519")] string symbol)
    {
        _logger.LogInformation("调用 GetKeyMetrics: {Symbol}", symbol);
        var cacheKey = $"metrics:{symbol}";
        var result = await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetKeyMetricsAsync(symbol),
            TimeSpan.FromMinutes(30));

        if (result == null)
            return $"未找到股票 {symbol} 的财务数据。";

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("get_profit_analysis")]
    [Description("对公司的盈利能力进行简单分析，基于已有财务指标判断公司的盈利质量。")]
    [return: Description("盈利能力分析文本")]
    public async Task<string> GetProfitAnalysisAsync(
        [Description("股票代码")] string symbol)
    {
        _logger.LogInformation("调用 GetProfitAnalysis: {Symbol}", symbol);
        var metrics = await _stockService.GetKeyMetricsAsync(symbol);
        if (metrics == null)
            return $"未找到股票 {symbol} 的财务数据。";

        var analysis = new List<string>();
        if (metrics.ROE > 20) analysis.Add($"ROE {metrics.ROE}% 处于优秀水平");
        else if (metrics.ROE > 10) analysis.Add($"ROE {metrics.ROE}% 处于良好水平");
        else analysis.Add($"ROE {metrics.ROE}% 需要关注盈利能力");

        if (metrics.RevenueGrowth > 20) analysis.Add($"营收增长率 {metrics.RevenueGrowth}% 高速增长");
        else if (metrics.RevenueGrowth > 5) analysis.Add($"营收增长率 {metrics.RevenueGrowth}% 稳健增长");
        else analysis.Add($"营收增长率 {metrics.RevenueGrowth}% 增长放缓");

        if (metrics.DebtRatio < 40) analysis.Add($"资产负债率 {metrics.DebtRatio}% 财务风险较低");
        else if (metrics.DebtRatio < 70) analysis.Add($"资产负债率 {metrics.DebtRatio}% 财务风险适中");
        else analysis.Add($"资产负债率 {metrics.DebtRatio}% 需关注债务风险");

        return JsonSerializer.Serialize(new { Symbol = symbol, Analysis = string.Join("；", analysis) },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
