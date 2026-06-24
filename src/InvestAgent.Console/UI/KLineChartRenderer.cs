using System.Text;
using InvestAgent.Core.Models;
using Spectre.Console;

namespace InvestAgent.Console.UI;

/// <summary>
/// 终端 K 线图渲染器。
/// 使用 Unicode 字符在控制台中绘制蜡烛图（Candlestick Chart），
/// 支持价格通道、成交量柱状图和日期标注。
/// 中国习惯：红色表示上涨（阳线），绿色表示下跌（阴线）。
/// </summary>
public static class KLineChartRenderer
{
    /// <summary>价格通道高度（字符行数）</summary>
    private const int ChartHeight = 18;

    /// <summary>成交量区域高度</summary>
    private const int VolumeHeight = 4;

    /// <summary>每根 K 线最小宽度（字符列数）</summary>
    private const int MinCandleWidth = 2;

    /// <summary>
    /// 渲染 K 线图到控制台。
    /// </summary>
    /// <param name="data">K 线数据列表</param>
    /// <param name="title">图表标题</param>
    public static void Render(IReadOnlyList<StockKLine> data, string title = "K线图")
    {
        if (data.Count < 2) return;

        // 根据终端宽度采样数据点
        var displayWidth = System.Console.WindowWidth > 0 ? System.Console.WindowWidth - 12 : 100;
        var candles = SampleData(data, displayWidth / MinCandleWidth);

        var high = candles.Max(c => c.High);
        var low = candles.Min(c => c.Low);
        var range = high - low;
        if (range == 0) range = 1;
        var maxVol = candles.Max(c => c.Volume);

        // 构建像素网格（行=价格轴+成交量，列=K线条数）
        var grid = new char[ChartHeight + VolumeHeight + 1, candles.Count];
        for (int y = 0; y < grid.GetLength(0); y++)
            for (int x = 0; x < grid.GetLength(1); x++)
                grid[y, x] = ' ';

        // ── 绘制蜡烛体 ──────────────────────────────────
        for (int x = 0; x < candles.Count; x++)
        {
            var c = candles[x];
            int highY = (int)((high - c.High) / range * (ChartHeight - 1));
            int lowY = (int)((high - c.Low) / range * (ChartHeight - 1));
            int openY = (int)((high - c.Open) / range * (ChartHeight - 1));
            int closeY = (int)((high - c.Close) / range * (ChartHeight - 1));
            bool bullish = c.Close >= c.Open;

            int bodyTop = Math.Min(openY, closeY);
            int bodyBot = Math.Max(openY, closeY);

            // 影线（wick）：从最高到实体顶部，从实体底部到最低
            for (int y = Math.Max(0, highY); y <= Math.Min(ChartHeight - 1, bodyTop); y++)
                grid[y, x] = '│';
            for (int y = Math.Max(0, bodyBot); y <= Math.Min(ChartHeight - 1, lowY); y++)
                grid[y, x] = '│';

            // 实体：阳线 • 阴线 ▓
            for (int y = Math.Max(0, bodyTop); y <= Math.Min(ChartHeight - 1, bodyBot); y++)
                grid[y, x] = bullish ? '█' : '▓';

            // 成交量柱
            decimal volRatio = maxVol > 0 ? c.Volume / maxVol : 0;
            int volHeight = (int)(volRatio * VolumeHeight);
            for (int vy = 0; vy < volHeight; vy++)
            {
                int gy = ChartHeight + 1 + (VolumeHeight - 1 - vy);
                if (gy < grid.GetLength(0))
                    grid[gy, x] = bullish ? '▄' : '▄';
            }
        }

        // ── 渲染到 Spectre.Console ──────────────────────
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"  [bold]{title.EscapeMarkup()}[/] [dim]({data.Count}条数据, 显示{candles.Count}根K线)[/]");

        // 价格轴刻度
        decimal yStep = range / (ChartHeight - 1);
        for (int y = 0; y < ChartHeight; y++)
        {
            decimal price = high - yStep * y;
            var priceLabel = $" {price,10:F2} ";
            sb.Append(priceLabel);

            for (int x = 0; x < candles.Count; x++)
            {
                char cell = grid[y, x];
                sb.Append(cell switch
                {
                    '█' => $"[red]█[/]",     // 中国习惯: 红涨(阳线)
                    '▓' => $"[green]▓[/]",   // 中国习惯: 绿跌(阴线)
                    '│' => "[grey]│[/]",
                    _ => " "
                });
            }
            sb.AppendLine();
        }

        // X 轴分隔线
        sb.Append(new string(' ', 13));
        sb.Append(new string('─', candles.Count));
        sb.AppendLine();

        // 成交量区域
        for (int y = ChartHeight + 1; y < ChartHeight + 1 + VolumeHeight; y++)
        {
            sb.Append(new string(' ', 13));
            for (int x = 0; x < candles.Count; x++)
            {
                char cell = grid[y, x];
                bool bullish = candles[x].Close >= candles[x].Open;
                sb.Append(cell == '▄' ? (bullish ? "[red]▄[/]" : "[green]▄[/]") : " ");
            }
            sb.AppendLine();
        }

        // 成交量标签
        sb.Append(new string(' ', 13));
        sb.AppendLine("[dim]成交量[/]");

        // 日期标注（采样显示）
        sb.Append(new string(' ', 13));
        int labelStep = Math.Max(1, candles.Count / 8);
        for (int x = 0; x < candles.Count; x++)
        {
            if (x % labelStep == 0 && x < candles.Count - 1)
            {
                var dateStr = candles[x].Date.ToString("yyMMdd");
                sb.Append($"[dim]{dateStr[..2]}[/]");
            }
            else if (x == candles.Count - 1)
            {
                sb.Append("[dim]→[/]");
            }
            else sb.Append(' ');
        }
        sb.AppendLine();

        // 区间统计
        sb.AppendLine();
        var first = data.First();
        var last = data.Last();
        var change = last.Close - first.Close;
        var changePct = first.Close != 0 ? change / first.Close * 100 : 0;
        var changeColor = change >= 0 ? "red" : "green";
        sb.AppendLine($"  [dim]区间: {first.Date:yyyy-MM-dd} ~ {last.Date:yyyy-MM-dd}[/]  " +
                      $"[dim]涨跌:[/] [{changeColor}]{change:+#.##;- #.##;0} ({changePct:+#.##;- #.##;0}%)[/]");

        AnsiConsole.Markup(sb.ToString());
    }

    /// <summary>
    /// 数据采样——当数据点超过显示宽度时，等间距抽样。
    /// 始终保留首尾两点以确保走势完整。
    /// </summary>
    private static List<StockKLine> SampleData(IReadOnlyList<StockKLine> data, int maxCandles)
    {
        if (data.Count <= maxCandles) return data.ToList();

        var result = new List<StockKLine> { data[0] };
        float step = (float)(data.Count - 2) / (maxCandles - 2);
        for (int i = 1; i < maxCandles - 1; i++)
            result.Add(data[(int)(1 + step * i)]);
        result.Add(data[^1]);
        return result;
    }

    /// <summary>
    /// 检测 Observation 返回的 JSON 是否包含 K 线数据。
    /// 支持大小写字段名的 JSON 数组（兼容不同数据源）。
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <param name="data">解析出的 K 线数据列表</param>
    /// <returns>是否为有效的 K 线数据</returns>
    public static bool TryParseKLineJson(string json, out List<StockKLine> data)
    {
        data = new List<StockKLine>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            // 检查必要字段（支持大小写两种格式）
            if (!first.TryGetProperty("open", out _) && !first.TryGetProperty("Open", out _)) return false;

            foreach (var item in root.EnumerateArray())
            {
                data.Add(new StockKLine
                {
                    Date = item.TryGetProperty("date", out var d) || item.TryGetProperty("Date", out d)
                        ? d.GetDateTime() : DateTime.Now,
                    Open = GetDecimalProp(item, "open") ?? GetDecimalProp(item, "Open") ?? 0,
                    High = GetDecimalProp(item, "high") ?? GetDecimalProp(item, "High") ?? 0,
                    Low = GetDecimalProp(item, "low") ?? GetDecimalProp(item, "Low") ?? 0,
                    Close = GetDecimalProp(item, "close") ?? GetDecimalProp(item, "Close") ?? 0,
                    Volume = GetDecimalProp(item, "volume") ?? GetDecimalProp(item, "Volume") ?? 0
                });
            }
            return data.Count >= 2;
        }
        catch
        {
            return false;
        }
    }

    private static decimal? GetDecimalProp(System.Text.Json.JsonElement elem, string name)
    {
        if (elem.TryGetProperty(name, out var val) && val.ValueKind == System.Text.Json.JsonValueKind.Number)
            return val.GetDecimal();
        return null;
    }
}
