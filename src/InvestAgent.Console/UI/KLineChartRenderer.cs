using System.Text;
using InvestAgent.Core.Models;
using Spectre.Console;

namespace InvestAgent.Console.UI;

public static class KLineChartRenderer
{
    private const int ChartHeight = 18;
    private const int VolumeHeight = 4;
    private const int MinCandleWidth = 2;

    public static void Render(IReadOnlyList<StockKLine> data, string title = "K线图")
    {
        if (data.Count < 2) return;

        // Sample data if too many points for the display width
        var displayWidth = System.Console.WindowWidth > 0 ? System.Console.WindowWidth - 12 : 100;
        var candles = SampleData(data, displayWidth / MinCandleWidth);

        var high = candles.Max(c => c.High);
        var low = candles.Min(c => c.Low);
        var range = high - low;
        if (range == 0) range = 1;
        var maxVol = candles.Max(c => c.Volume);

        // Build the chart grid
        var grid = new char[ChartHeight + VolumeHeight + 1, candles.Count];
        for (int y = 0; y < grid.GetLength(0); y++)
            for (int x = 0; x < grid.GetLength(1); x++)
                grid[y, x] = ' ';

        // Draw candles
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

            // Wick: from highY to bodyTop and from bodyBot to lowY
            for (int y = Math.Max(0, highY); y <= Math.Min(ChartHeight - 1, bodyTop); y++)
                grid[y, x] = '│';
            for (int y = Math.Max(0, bodyBot); y <= Math.Min(ChartHeight - 1, lowY); y++)
                grid[y, x] = '│';

            // Body: thick bar for open-close range
            for (int y = Math.Max(0, bodyTop); y <= Math.Min(ChartHeight - 1, bodyBot); y++)
                grid[y, x] = bullish ? '█' : '▓';

            // Volume bars below the price chart
            decimal volRatio = maxVol > 0 ? c.Volume / maxVol : 0;
            int volHeight = (int)(volRatio * VolumeHeight);
            for (int vy = 0; vy < volHeight; vy++)
            {
                int gy = ChartHeight + 1 + (VolumeHeight - 1 - vy);
                if (gy < grid.GetLength(0))
                    grid[gy, x] = bullish ? '▄' : '▄';
            }
        }

        // Render to Spectre.Console
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"  [bold]{title.EscapeMarkup()}[/] [dim]({data.Count}条数据, 显示{candles.Count}根K线)[/]");

        decimal yStep = range / (ChartHeight - 1);
        for (int y = 0; y < ChartHeight; y++)
        {
            decimal price = high - yStep * y;
            var priceLabel = $" {price,10:F2} ";
            sb.Append(priceLabel);

            for (int x = 0; x < candles.Count; x++)
            {
                char cell = grid[y, x];
                bool bullish = candles[x].Close >= candles[x].Open;
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

        // X-axis separator
        sb.Append(new string(' ', 13));
        sb.Append(new string('─', candles.Count));
        sb.AppendLine();

        // Volume area
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

        // Volume label
        sb.Append(new string(' ', 13));
        sb.AppendLine("[dim]成交量[/]");

        // Date labels (sample a few)
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

        // Price stats
        sb.AppendLine();
        var first = data.First();
        var last = data.Last();
        var change = last.Close - first.Close;
        var changePct = first.Close != 0 ? change / first.Close * 100 : 0;
        var changeColor = change >= 0 ? "red" : "green";  // 中国: 红涨绿跌
        sb.AppendLine($"  [dim]区间: {first.Date:yyyy-MM-dd} ~ {last.Date:yyyy-MM-dd}[/]  " +
                      $"[dim]涨跌:[/] [{changeColor}]{change:+#.##;- #.##;0} ({changePct:+#.##;- #.##;0}%)[/]");

        AnsiConsole.Markup(sb.ToString());
    }

    private static List<StockKLine> SampleData(IReadOnlyList<StockKLine> data, int maxCandles)
    {
        if (data.Count <= maxCandles) return data.ToList();

        // Take evenly spaced samples, always include first and last
        var result = new List<StockKLine> { data[0] };
        float step = (float)(data.Count - 2) / (maxCandles - 2);
        for (int i = 1; i < maxCandles - 1; i++)
            result.Add(data[(int)(1 + step * i)]);
        result.Add(data[^1]);
        return result;
    }

    /// <summary>检测 Observation 返回的 JSON 是否包含 K 线数据</summary>
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
