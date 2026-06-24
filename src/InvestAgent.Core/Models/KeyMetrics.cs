namespace InvestAgent.Core.Models;

/// <summary>
/// 公司核心财务指标数据模型。
/// 包含估值指标（PE/PB）、盈利能力（ROE/ROA）、成长性、负债结构和市值等。
/// </summary>
public class KeyMetrics
{
    /// <summary>股票代码</summary>
    public string Symbol { get; set; } = "";

    /// <summary>股票名称</summary>
    public string Name { get; set; } = "";

    /// <summary>市盈率（PE），反映估值水平</summary>
    public decimal PE { get; set; }

    /// <summary>市净率（PB），反映资产估值水平</summary>
    public decimal PB { get; set; }

    /// <summary>净资产收益率（ROE），衡量股东权益回报率</summary>
    public decimal ROE { get; set; }

    /// <summary>总资产收益率（ROA），衡量总资产运用效率</summary>
    public decimal ROA { get; set; }

    /// <summary>毛利率，反映产品或服务的基本盈利能力</summary>
    public decimal GrossMargin { get; set; }

    /// <summary>净利率，反映最终净利润占营收比例</summary>
    public decimal NetMargin { get; set; }

    /// <summary>营收增长率（同比）</summary>
    public decimal RevenueGrowth { get; set; }

    /// <summary>净利润增长率（同比）</summary>
    public decimal ProfitGrowth { get; set; }

    /// <summary>资产负债率，反映财务杠杆水平</summary>
    public decimal DebtRatio { get; set; }

    /// <summary>总市值（元）</summary>
    public decimal MarketCap { get; set; }

    /// <summary>财务报告日期</summary>
    public DateTime ReportDate { get; set; }
}
