using System.ComponentModel;
using System.Text.Json;
using InvestAgent.Core.Memory;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

/// <summary>
/// 技术分析插件。
/// 为 LLM Agent 提供 MA（移动平均线）、RSI（相对强弱指数）、MACD
/// 三大经典技术指标的计算功能，以及基于三者的综合交易信号生成。
/// 所有指标计算均使用标准的金融数学公式。
/// </summary>
public class TechnicalAnalysisPlugin
{
    private readonly IStockDataService _stockService;
    private readonly IWorkingMemory _workingMemory;
    private readonly ILogger<TechnicalAnalysisPlugin> _logger;

    public TechnicalAnalysisPlugin(IStockDataService stockService, IWorkingMemory workingMemory, ILogger<TechnicalAnalysisPlugin> logger)
    {
        _stockService = stockService;
        _workingMemory = workingMemory;
        _logger = logger;
    }

    /// <summary>
    /// 获取K线数据（带缓存）。作为所有技术指标计算的基础数据源。
    /// </summary>
    /// <param name="symbol">股票代码</param>
    /// <param name="days">获取天数（默认 60）</param>
    private async Task<List<StockKLine>> GetKLineDataAsync(string symbol, int days = 60)
    {
        var cacheKey = $"kline:{symbol}:{days}";
        return await _workingMemory.GetOrSetAsync(cacheKey,
            async () => await _stockService.GetHistoricalPricesAsync(symbol, days),
            TimeSpan.FromMinutes(5)) ?? new List<StockKLine>();
    }

    /// <summary>
    /// 计算移动平均线（MA）并判断金叉/死叉信号。
    /// 比较当前价格与均线的关系，以及价格是否刚刚穿越均线。
    /// </summary>
    [KernelFunction("calculate_ma")]
    [Description("计算指定股票的移动平均线(MA)。判断当前价格与均线的关系，以及是否存在金叉或死叉信号。")]
    [return: Description("移动平均线分析结果JSON")]
    public async Task<string> CalculateMAAsync(
        [Description("股票代码")] string symbol,
        [Description("均线周期，默认20日")] int period = 20)
    {
        _logger.LogInformation("计算 {Symbol} MA({Period})", symbol, period);
        var klines = await GetKLineDataAsync(symbol, Math.Max(period + 10, 60));

        if (klines.Count < period)
            return "K线数据不足，无法计算均线。";

        var closes = klines.Select(k => k.Close).ToList();

        // 使用滑动窗口计算每个位置的 MA 值
        var maValues = new List<decimal>();
        for (int i = period - 1; i < closes.Count; i++)
            maValues.Add(closes.Skip(i - period + 1).Take(period).Average());

        var currentPrice = closes.Last();
        var currentMA = maValues.Last();
        var prevPrice = closes[^2]; // 前一日收盘价
        var prevMA = maValues[^2];   // 前一日 MA 值

        var result = new MAResult
        {
            Symbol = symbol,
            Period = period,
            MAValue = Math.Round(currentMA, 2),
            CurrentPrice = currentPrice,
            Position = currentPrice > currentMA ? "价格在均线上方" : "价格在均线下方",
            // 金叉：前一日价格 ≤ 均线 且 当日价格 > 均线
            GoldenCross = prevPrice <= prevMA && currentPrice > currentMA,
            // 死叉：前一日价格 ≥ 均线 且 当日价格 < 均线
            DeadCross = prevPrice >= prevMA && currentPrice < currentMA
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 计算相对强弱指数（RSI）。
    /// 使用威尔德（Wilder）的经典公式：RSI = 100 - 100/(1+RS)，其中 RS = 平均涨幅 / 平均跌幅。
    /// </summary>
    [KernelFunction("calculate_rsi")]
    [Description("计算指定股票的RSI(相对强弱指数)。RSI>70为超买区，RSI<30为超卖区。")]
    [return: Description("RSI分析结果JSON")]
    public async Task<string> CalculateRSIAsync(
        [Description("股票代码")] string symbol,
        [Description("RSI周期，默认14日")] int period = 14)
    {
        _logger.LogInformation("计算 {Symbol} RSI({Period})", symbol, period);
        var klines = await GetKLineDataAsync(symbol, period + 20);

        if (klines.Count < period + 1)
            return "K线数据不足，无法计算RSI。";

        var closes = klines.Select(k => k.Close).ToList();
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        // 计算每日的涨跌幅，分为涨幅和跌幅两组
        for (int i = 1; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            gains.Add(diff > 0 ? diff : 0);
            losses.Add(diff < 0 ? -diff : 0);
        }

        // 使用最近 period 个交易日的平均值
        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();
        var rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
        var rsi = avgLoss == 0 ? 100m : 100m - (100m / (1m + rs));

        string status = rsi switch
        {
            > 80 => "严重超买",
            > 70 => "超买",
            > 50 => "中性偏强",
            > 30 => "中性偏弱",
            > 20 => "超卖",
            _ => "严重超卖"
        };

        return JsonSerializer.Serialize(new RSIResult
        {
            Symbol = symbol,
            Period = period,
            RSIValue = Math.Round(rsi, 2),
            Status = status
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 计算 MACD 指标。
    /// 使用标准参数：快线 EMA12、慢线 EMA26、信号线 DEA(9)。
    /// 同时判断金叉（DIF上穿DEA）和死叉（DIF下穿DEA）。
    /// </summary>
    [KernelFunction("calculate_macd")]
    [Description("计算指定股票的MACD指标。包含DIF、DEA和MACD柱的值，以及金叉/死叉判断。")]
    [return: Description("MACD分析结果JSON")]
    public async Task<string> CalculateMACDAsync(
        [Description("股票代码")] string symbol)
    {
        _logger.LogInformation("计算 {Symbol} MACD", symbol);
        var klines = await GetKLineDataAsync(symbol, 60);

        if (klines.Count < 26)
            return "K线数据不足，无法计算MACD。";

        var closes = klines.Select(k => k.Close).ToList();

        // MACD 标准参数：EMA12 > EMA26 > DEA(9)
        var ema12 = CalculateEMA(closes, 12);
        var ema26 = CalculateEMA(closes, 26);

        // DIF = EMA12 - EMA26
        var dif = ema12.Zip(ema26, (a, b) => a - b).ToList();

        // DEA = DIF 的 9 日 EMA（信号线）
        var dea = CalculateEMA(dif, 9);

        // MACD 柱 = 2 × (DIF - DEA)
        var macd = dif.Zip(dea, (d, e) => 2 * (d - e)).ToList();

        var currentDIF = dif.Last();
        var currentDEA = dea.Last();
        var currentMACD = macd.Last();
        var prevDIF = dif[^2];
        var prevDEA = dea[^2];

        return JsonSerializer.Serialize(new MACDResult
        {
            Symbol = symbol,
            DIF = Math.Round(currentDIF, 4),
            DEA = Math.Round(currentDEA, 4),
            MACDHistogram = Math.Round(currentMACD, 4),
            // 金叉：前一日 DIF ≤ DEA 且 当日 DIF > DEA
            GoldenCross = prevDIF <= prevDEA && currentDIF > currentDEA,
            // 死叉：前一日 DIF ≥ DEA 且 当日 DIF < DEA
            DeadCross = prevDIF >= prevDEA && currentDIF < currentDEA
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 综合 MA、RSI、MACD 三大指标生成交易信号。
    /// 使用评分制：MA 多头 +2，RSI 健康 +1，MACD 多头 +2。
    /// 信号等级：买入(≥4)、持有偏多(≥2)、卖出偏空(<2)。
    /// </summary>
    [KernelFunction("generate_trading_signal")]
    [Description("综合移动平均线、RSI和MACD三大指标，生成综合交易信号(买入/卖出/持有)及置信度。注意：此信号仅为参考，不构成投资建议。")]
    [return: Description("综合交易信号JSON")]
    public async Task<string> GenerateTradingSignalAsync(
        [Description("股票代码")] string symbol)
    {
        _logger.LogInformation("生成 {Symbol} 综合交易信号", symbol);
        var klines = await GetKLineDataAsync(symbol, 60);

        if (klines.Count < 30)
            return "数据不足，无法生成交易信号。";

        var closes = klines.Select(k => k.Close).ToList();

        // ── MA 信号 ─────────────────────────────────────
        // 计算 20 日和 5 日均线，判断多头排列
        var ma20 = closes.TakeLast(20).Average();
        var ma5 = closes.TakeLast(5).Average();
        var currentPrice = closes.Last();
        bool maBullish = ma5 > ma20 && currentPrice > ma20;

        // ── RSI 信号 ─────────────────────────────────────
        // 重新计算 RSI（14日），健康区间 40-70
        var rsi = await _stockService.GetHistoricalPricesAsync(symbol, 30);
        var rsiCloses = rsi.Select(k => k.Close).ToList();
        var gains = new List<decimal>();
        var losses = new List<decimal>();
        for (int i = 1; i < rsiCloses.Count; i++)
        {
            var diff = rsiCloses[i] - rsiCloses[i - 1];
            if (diff > 0) gains.Add(diff); else losses.Add(-diff);
        }
        decimal avgG = gains.Count > 0 ? gains.TakeLast(14).DefaultIfEmpty().Average() : 0;
        decimal avgL = losses.Count > 0 ? losses.TakeLast(14).DefaultIfEmpty().Average() : 0;
        decimal rs = avgL == 0 ? 100 : avgG / avgL;
        decimal rsiValue = avgL == 0 ? 100 : 100 - (100 / (1 + rs));
        bool rsiHealthy = rsiValue > 40 && rsiValue < 70;

        // ── MACD 信号 ────────────────────────────────────
        var ema12 = CalculateEMA(closes, 12);
        var ema26 = CalculateEMA(closes, 26);
        var dif = ema12.Zip(ema26, (a, b) => a - b).ToList();
        var dea = CalculateEMA(dif, 9);
        bool macdBullish = dif.Last() > dea.Last();

        // ── 综合评分 ─────────────────────────────────────
        var reasons = new List<string>();
        int bullishScore = 0;

        if (maBullish) { bullishScore += 2; reasons.Add("MA多头排列"); }
        else reasons.Add("MA偏空");

        if (rsiHealthy) { bullishScore += 1; reasons.Add($"RSI={rsiValue:F1} 处于健康区间"); }
        else if (rsiValue >= 70) reasons.Add($"RSI={rsiValue:F1} 超买,需谨慎");
        else reasons.Add($"RSI={rsiValue:F1} 偏弱");

        if (macdBullish) { bullishScore += 2; reasons.Add("MACD金叉/多头"); }
        else reasons.Add("MACD偏空");

        string signal = bullishScore switch
        {
            >= 4 => "买入",
            >= 2 => "持有(偏多)",
            _ => "卖出(偏空)"
        };
        int confidence = Math.Min(95, bullishScore * 20 + 30);

        return JsonSerializer.Serialize(new TradingSignal
        {
            Symbol = symbol,
            Signal = signal,
            Confidence = confidence,
            Reasons = reasons
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 计算指数移动平均线（EMA）。
    /// 公式：EMA_t = (Price_t - EMA_{t-1}) × Multiplier + EMA_{t-1}
    /// 其中 Multiplier = 2 / (period + 1)
    /// </summary>
    /// <param name="values">价格序列</param>
    /// <param name="period">EMA 周期</param>
    /// <returns>与输入等长的 EMA 序列（前几个值使用简单平均作为种子）</returns>
    private static List<decimal> CalculateEMA(List<decimal> values, int period)
    {
        var ema = new List<decimal>();
        decimal multiplier = 2m / (period + 1);

        for (int i = 0; i < values.Count; i++)
        {
            if (i == 0)
                ema.Add(values[i]); // 初始值用第一个价格
            else if (i < period)
                ema.Add(values.Take(i + 1).Average()); // 不足周期时用 SMA 近似
            else
                ema.Add((values[i] - ema[i - 1]) * multiplier + ema[i - 1]); // 标准 EMA 公式
        }
        return ema;
    }
}
