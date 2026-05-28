namespace InvestAgent.Core.Models;

public class CapitalFlowItem
{
    public string Symbol { get; set; } = "";
    public DateTime Date { get; set; }
    public string Source { get; set; } = "";
    public bool IsApproximate { get; set; }
    public bool IsDataAvailable { get; set; } = true;
    public string DataNote { get; set; } = "";
    /// <summary>主力净流入（元）</summary>
    public decimal MainForce { get; set; }
    /// <summary>超大单净流入（元）</summary>
    public decimal SuperLargeOrder { get; set; }
    /// <summary>大单净流入（元）</summary>
    public decimal LargeOrder { get; set; }
    /// <summary>中单净流入（元）</summary>
    public decimal MediumOrder { get; set; }
    /// <summary>小单净流入（元）</summary>
    public decimal SmallOrder { get; set; }
    /// <summary>合计净流入（元）</summary>
    public decimal TotalInflow => MainForce;
    /// <summary>显示友好的金额文字</summary>
    public string TotalInflowText => TotalInflow switch
    {
        >= 100000000 or <= -100000000 => $"{TotalInflow / 100000000m:F2}亿",
        >= 10000 or <= -10000 => $"{TotalInflow / 10000m:F0}万",
        _ => $"{TotalInflow:F0}"
    };
}
