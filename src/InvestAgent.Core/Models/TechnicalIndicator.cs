namespace InvestAgent.Core.Models;

public class MAResult
{
    public string Symbol { get; set; } = "";
    public int Period { get; set; }
    public decimal MAValue { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Position { get; set; } = ""; // "上方" or "下方"
    public bool GoldenCross { get; set; }
    public bool DeadCross { get; set; }
}

public class RSIResult
{
    public string Symbol { get; set; } = "";
    public int Period { get; set; }
    public decimal RSIValue { get; set; }
    public string Status { get; set; } = ""; // "超买", "超卖", "中性偏强", "中性偏弱"
}

public class MACDResult
{
    public string Symbol { get; set; } = "";
    public decimal DIF { get; set; }
    public decimal DEA { get; set; }
    public decimal MACDHistogram { get; set; }
    public bool GoldenCross { get; set; }
    public bool DeadCross { get; set; }
}

public class TradingSignal
{
    public string Symbol { get; set; } = "";
    public string Signal { get; set; } = ""; // "买入", "卖出", "持有"
    public int Confidence { get; set; }
    public List<string> Reasons { get; set; } = new();
}
