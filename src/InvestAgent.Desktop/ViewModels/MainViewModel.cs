using InvestAgent.Core.Agent;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using InvestAgent.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;

namespace InvestAgent.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private const double CanvasWidth = 1100;
    private const double CanvasHeight = 320;
    private const double PlotLeft = 60;
    private const double PlotTop = 20;
    private const double PlotRight = 20;
    private const double PlotBottom = 40;

    private readonly InvestAgentLoop _agent;
    private readonly IStockDataService _stockDataService;
    private readonly IAnalysisHistoryRepository _historyRepository;
    private string _userInput = "600519";
    private string _finalResponse = "";
    private string _resolvedSymbol = "";
    private string _metricsSummary = "";
    private string _mainBusiness = "";
    private bool _showDailyChart = true;
    private bool _showMonthlyChart = true;
    private AnalysisHistoryListItem? _selectedHistoryItem;

    public ObservableCollection<StepItemViewModel> Steps { get; } = new();
    public ObservableCollection<StockKLine> DailyKLines { get; } = new();
    public ObservableCollection<StockKLine> MonthlyKLines { get; } = new();
    public ObservableCollection<NewsItem> NewsItems { get; } = new();
    public ObservableCollection<NewsItem> CompanyNewsItems { get; } = new();
    public ObservableCollection<NewsItem> IndustryNewsItems { get; } = new();
    public ObservableCollection<KeyMetrics> FinancialHistoryItems { get; } = new();
    public ObservableCollection<AnalysisHistoryListItem> HistoryItems { get; } = new();

    public ObservableCollection<ChartSegment> DailySegments { get; } = new();
    public ObservableCollection<ChartSegment> MonthlySegments { get; } = new();
    public ObservableCollection<AxisTick> DailyYTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyYTicks { get; } = new();
    public ObservableCollection<AxisTick> DailyXTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyXTicks { get; } = new();

    public ICommand RunCommand { get; }
    public ICommand ToggleDailyViewCommand { get; }
    public ICommand ToggleMonthlyViewCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand LoadSelectedHistoryCommand { get; }

    public MainViewModel(InvestAgentLoop agent, IStockDataService stockDataService, IAnalysisHistoryRepository historyRepository)
    {
        _agent = agent;
        _stockDataService = stockDataService;
        _historyRepository = historyRepository;
        RunCommand = new RelayCommand(RunAsync, () => !string.IsNullOrWhiteSpace(UserInput));
        ToggleDailyViewCommand = new RelayCommand(() => { ShowDailyChart = !ShowDailyChart; return Task.CompletedTask; });
        ToggleMonthlyViewCommand = new RelayCommand(() => { ShowMonthlyChart = !ShowMonthlyChart; return Task.CompletedTask; });
        RefreshHistoryCommand = new RelayCommand(RefreshHistoryAsync);
        LoadSelectedHistoryCommand = new RelayCommand(LoadSelectedHistoryAsync);
        _ = RefreshHistoryAsync();
    }

    public string UserInput { get => _userInput; set { _userInput = value; OnPropertyChanged(); } }
    public string FinalResponse { get => _finalResponse; set { _finalResponse = value; OnPropertyChanged(); } }
    public string ResolvedSymbol { get => _resolvedSymbol; set { _resolvedSymbol = value; OnPropertyChanged(); } }
    public string MetricsSummary { get => _metricsSummary; set { _metricsSummary = value; OnPropertyChanged(); } }
    public string MainBusiness { get => _mainBusiness; set { _mainBusiness = value; OnPropertyChanged(); } }
    public bool ShowDailyChart { get => _showDailyChart; set { _showDailyChart = value; OnPropertyChanged(); } }
    public bool ShowMonthlyChart { get => _showMonthlyChart; set { _showMonthlyChart = value; OnPropertyChanged(); } }
    public AnalysisHistoryListItem? SelectedHistoryItem { get => _selectedHistoryItem; set { _selectedHistoryItem = value; OnPropertyChanged(); } }

    private async Task RunAsync()
    {
        try
        {
            if (!TryNormalizeTargetInput(UserInput, out var targetInput, out var inputError))
            {
                Steps.Add(new StepItemViewModel { Title = "输入校验", Content = inputError });
                FinalResponse = inputError;
                return;
            }

            ResetUiData();
            FinalResponse = "";
            MetricsSummary = "";
            MainBusiness = "";

            var symbol = await ResolveSymbolAsync(targetInput);
            ResolvedSymbol = symbol;
            var financialHistory = await LoadMandatoryDataAsync(symbol);

            BuildChart(DailyKLines.ToList(), DailySegments, DailyYTicks, DailyXTicks);
            BuildChart(MonthlyKLines.ToList(), MonthlySegments, MonthlyYTicks, MonthlyXTicks);

            MainBusiness = await SafeLoadAsync(() => _stockDataService.GetMainBusinessAsync(symbol), "主营业务信息暂不可用。");

            var aRequirements = BuildAgentARequirements(targetInput, symbol);
            Steps.Add(new StepItemViewModel { Title = "Agent A | 任务拆解", Content = aRequirements });

            var bAnalysis = AnalyzeKLineAgentB(symbol);
            Steps.Add(new StepItemViewModel { Title = "Agent B | K线分析", Content = bAnalysis });

            var cAnalysis = AnalyzeNewsAgentC(symbol);
            Steps.Add(new StepItemViewModel { Title = "Agent C | 新闻分析", Content = cAnalysis });

            var dAnalysis = AnalyzeFinancialAgentD(symbol, financialHistory);
            Steps.Add(new StepItemViewModel { Title = "Agent D | 财务分析", Content = dAnalysis });

            var finalPrompt = BuildAgentAFinalPrompt(targetInput, symbol, MainBusiness, bAnalysis, cAnalysis, dAnalysis);
            FinalResponse = await RunAgentAAsync(finalPrompt);
            Steps.Add(new StepItemViewModel { Title = "Agent A | 最终汇总", Content = "已汇总 B/C/D 分析并生成最终答复。" });

            await _historyRepository.SaveAsync(new AnalysisHistoryRecord
            {
                Symbol = symbol,
                StockName = FinancialHistoryItems.FirstOrDefault()?.Name ?? "",
                MainBusiness = MainBusiness,
                FinalResponse = FinalResponse,
                MetricsSummary = MetricsSummary,
                AgentB = bAnalysis,
                AgentC = cAnalysis,
                AgentD = dAnalysis,
                CreatedAt = DateTime.Now,
                DailyKLines = DailyKLines.ToList(),
                MonthlyKLines = MonthlyKLines.ToList(),
                CompanyNews = CompanyNewsItems.ToList(),
                IndustryNews = IndustryNewsItems.ToList(),
                FinancialHistory = FinancialHistoryItems.ToList(),
                WorkflowSteps = Steps.Select(x => new HistoryStepItem { Title = x.Title, Content = x.Content }).ToList()
            });
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            Steps.Add(new StepItemViewModel { Title = "Error", Content = ex.ToString() });
            FinalResponse = $"分析失败：{ex.Message}";
        }
    }

    private void ResetUiData()
    {
        Steps.Clear(); DailyKLines.Clear(); MonthlyKLines.Clear(); NewsItems.Clear(); CompanyNewsItems.Clear(); IndustryNewsItems.Clear(); FinancialHistoryItems.Clear();
        DailySegments.Clear(); MonthlySegments.Clear(); DailyYTicks.Clear(); MonthlyYTicks.Clear(); DailyXTicks.Clear(); MonthlyXTicks.Clear();
    }

    private async Task RefreshHistoryAsync()
    {
        var list = await _historyRepository.ListAsync(300);
        HistoryItems.Clear();
        foreach (var item in list)
            HistoryItems.Add(item);
    }

    private async Task LoadSelectedHistoryAsync()
    {
        if (SelectedHistoryItem is null) return;
        var record = await _historyRepository.GetAsync(SelectedHistoryItem.Id);
        if (record is null) return;
        RestoreFromHistory(record);
    }

    private void RestoreFromHistory(AnalysisHistoryRecord record)
    {
        ResetUiData();
        UserInput = string.IsNullOrWhiteSpace(record.StockName) ? record.Symbol : $"{record.Symbol} {record.StockName}";
        ResolvedSymbol = record.Symbol;
        MainBusiness = record.MainBusiness;
        FinalResponse = record.FinalResponse;
        MetricsSummary = record.MetricsSummary;

        foreach (var d in record.DailyKLines.OrderBy(x => x.Date)) DailyKLines.Add(d);
        foreach (var m in record.MonthlyKLines.OrderBy(x => x.Date)) MonthlyKLines.Add(m);
        foreach (var n in record.CompanyNews.OrderByDescending(x => x.PublishTime)) { CompanyNewsItems.Add(n); NewsItems.Add(n); }
        foreach (var n in record.IndustryNews.OrderByDescending(x => x.PublishTime)) { IndustryNewsItems.Add(n); NewsItems.Add(n); }
        foreach (var f in record.FinancialHistory.OrderByDescending(x => x.ReportDate)) FinancialHistoryItems.Add(f);

        BuildChart(DailyKLines.ToList(), DailySegments, DailyYTicks, DailyXTicks);
        BuildChart(MonthlyKLines.ToList(), MonthlySegments, MonthlyYTicks, MonthlyXTicks);

        if (record.WorkflowSteps.Count > 0)
        {
            foreach (var s in record.WorkflowSteps)
                Steps.Add(new StepItemViewModel { Title = s.Title, Content = s.Content });
            Steps.Add(new StepItemViewModel { Title = "历史回放", Content = $"已恢复完整工作流：{record.Symbol} / {record.CreatedAt:yyyy-MM-dd HH:mm:ss}" });
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(record.AgentB)) Steps.Add(new StepItemViewModel { Title = "Agent B | K线分析(历史)", Content = record.AgentB });
            if (!string.IsNullOrWhiteSpace(record.AgentC)) Steps.Add(new StepItemViewModel { Title = "Agent C | 新闻分析(历史)", Content = record.AgentC });
            if (!string.IsNullOrWhiteSpace(record.AgentD)) Steps.Add(new StepItemViewModel { Title = "Agent D | 财务分析(历史)", Content = record.AgentD });
            Steps.Add(new StepItemViewModel { Title = "历史回放", Content = $"已加载历史分析记录：{record.Symbol} / {record.CreatedAt:yyyy-MM-dd HH:mm:ss}" });
        }
    }

    private async Task<string> ResolveSymbolAsync(string input)
    {
        var code = Regex.Match(input, @"\b\d{6}\b").Value;
        if (!string.IsNullOrWhiteSpace(code)) return code;
        var letters = Regex.Match(input, @"\b[A-Za-z]{1,6}\b").Value;
        if (!string.IsNullOrWhiteSpace(letters)) return letters.ToUpperInvariant();

        var results = await _stockDataService.SearchStockAsync(input);
        var picked = PickBestSymbol(results);
        if (!string.IsNullOrWhiteSpace(picked)) return picked;

        var chinese = Regex.Replace(input, @"[^\u4e00-\u9fa5]", "");
        if (!string.IsNullOrWhiteSpace(chinese))
        {
            var candidates = new HashSet<string>();
            for (int len = Math.Min(6, chinese.Length); len >= 2; len--)
            {
                for (int i = 0; i + len <= chinese.Length; i++)
                    candidates.Add(chinese.Substring(i, len));
            }
            foreach (var key in candidates.OrderByDescending(x => x.Length))
            {
                var r = await _stockDataService.SearchStockAsync(key);
                var s = PickBestSymbol(r);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }

        var englishTokens = Regex.Matches(input, @"[A-Za-z]{2,10}")
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
        foreach (var token in englishTokens)
        {
            var r = await _stockDataService.SearchStockAsync(token);
            var s = PickBestSymbol(r);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        return "600519";
    }

    private static bool TryNormalizeTargetInput(string input, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        var raw = (input ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "请输入股票代码（如 600519）或股票名称（如 贵州茅台）。";
            return false;
        }

        if (Regex.IsMatch(raw, @"^\d{6}$"))
        {
            normalized = raw;
            return true;
        }

        if (Regex.IsMatch(raw, @"^[A-Za-z]{1,10}$"))
        {
            normalized = raw.ToUpperInvariant();
            return true;
        }

        if (Regex.IsMatch(raw, @"^[\u4e00-\u9fa5A-Za-z0-9·\-\s]{2,30}$") &&
            !raw.Contains("请") && !raw.Contains("分析") && !raw.Contains("风险") && !raw.Contains("趋势"))
        {
            normalized = raw;
            return true;
        }

        error = "输入格式仅支持“股票代码”或“股票名称”，不再支持自然语言问句。";
        return false;
    }

    private static string PickBestSymbol(List<StockQuote> results)
    {
        if (results is null || results.Count == 0) return "";
        var first = results.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Symbol));
        return first?.Symbol ?? "";
    }

    private async Task<List<KeyMetrics>> LoadMandatoryDataAsync(string symbol)
    {
        var daily = await SafeLoadAsync(() => _stockDataService.GetHistoricalPricesAsync(symbol, 90), new List<StockKLine>());
        var monthly = await SafeLoadAsync(() => _stockDataService.GetMonthlyKLineAsync(symbol, 12), new List<StockKLine>());
        var news = await SafeLoadAsync(() => _stockDataService.GetLatestNewsAsync(symbol, 80), new List<NewsItem>
        {
            new() { Title = "新闻数据拉取失败", Summary = "请求新闻源时网络异常，已降级为空数据。", Source = "DesktopFallback", PublishTime = DateTime.Now, Sentiment = "neutral", IsDataAvailable = false, DataNote = "网络波动导致请求失败。" }
        });
        var financialHistory = await SafeLoadAsync(() => _stockDataService.GetKeyMetricsHistoryAsync(symbol, 4), new List<KeyMetrics>());
        var metrics = financialHistory.OrderByDescending(x => x.ReportDate).FirstOrDefault()
                      ?? await SafeLoadAsync(() => _stockDataService.GetKeyMetricsAsync(symbol), null as KeyMetrics);

        foreach (var item in daily.OrderBy(x => x.Date)) DailyKLines.Add(item);
        foreach (var item in monthly.OrderBy(x => x.Date)) MonthlyKLines.Add(item);
        var cutoff = DateTime.Today.AddMonths(-3);
        var recentNews = news.Where(x => x.PublishTime >= cutoff).OrderByDescending(x => x.PublishTime).ToList();

        foreach (var item in recentNews)
        {
            NewsItems.Add(item);
            if ((item.DataNote ?? "").Contains("行业新闻")) IndustryNewsItems.Add(item);
            else CompanyNewsItems.Add(item);
        }

        MetricsSummary = metrics is null ? "财务数据不可用。" : BuildFinancialSummary(metrics, financialHistory);
        foreach (var h in financialHistory.OrderByDescending(x => x.ReportDate)) FinancialHistoryItems.Add(h);
        return financialHistory;
    }

    private static string BuildFinancialSummary(KeyMetrics latest, List<KeyMetrics> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"名称: {latest.Name}");
        sb.AppendLine($"ROE: {latest.ROE:F2}%  ROA: {latest.ROA:F2}%");
        sb.AppendLine($"毛利率: {latest.GrossMargin:F2}%  净利率: {latest.NetMargin:F2}%");
        sb.AppendLine($"营收增长: {latest.RevenueGrowth:F2}%  净利增长: {latest.ProfitGrowth:F2}%  资产负债率: {latest.DebtRatio:F2}%");
        sb.AppendLine($"报告期: {latest.ReportDate:yyyy-MM-dd}");
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("最近一年财务序列:");
            foreach (var h in history.OrderByDescending(x => x.ReportDate))
                sb.AppendLine($"{h.ReportDate:yyyy-MM-dd} | ROE:{h.ROE:F2}% 毛利率:{h.GrossMargin:F2}% 净利率:{h.NetMargin:F2}% 营收增长:{h.RevenueGrowth:F2}% 净利增长:{h.ProfitGrowth:F2}% 负债率:{h.DebtRatio:F2}%");
        }
        return sb.ToString();
    }

    private static async Task<T> SafeLoadAsync<T>(Func<Task<T>> loader, T fallback)
    {
        try { return await loader(); } catch { return fallback; }
    }

    private static Brush GetRiseFallBrush(decimal prev, decimal curr) => curr >= prev ? Brushes.Red : Brushes.Green;

    private static void BuildChart(List<StockKLine> klines, ObservableCollection<ChartSegment> segments, ObservableCollection<AxisTick> yTicks, ObservableCollection<AxisTick> xTicks)
    {
        segments.Clear(); yTicks.Clear(); xTicks.Clear();
        if (klines.Count < 2) return;

        var prices = klines.Select(k => k.Close).ToList();
        var min = prices.Min();
        var max = prices.Max();
        var span = Math.Max(0.0001m, max - min);

        var plotW = CanvasWidth - PlotLeft - PlotRight;
        var plotH = CanvasHeight - PlotTop - PlotBottom;

        double X(int i) => PlotLeft + (klines.Count == 1 ? plotW / 2 : i * plotW / (klines.Count - 1));
        double Y(decimal p) => PlotTop + (double)((max - p) / span) * plotH;

        for (int i = 1; i < klines.Count; i++)
        {
            var prev = klines[i - 1].Close;
            var curr = klines[i].Close;
            segments.Add(new ChartSegment { X1 = X(i - 1), Y1 = Y(prev), X2 = X(i), Y2 = Y(curr), Stroke = GetRiseFallBrush(prev, curr) });
        }

        for (int t = 0; t <= 4; t++)
        {
            var v = max - t * span / 4;
            yTicks.Add(new AxisTick { X = 6, Y = PlotTop + t * plotH / 4 - 8, Label = v.ToString("F2") });
        }

        var tickIndexes = new[] { 0, klines.Count / 3, (klines.Count * 2) / 3, klines.Count - 1 }.Distinct().OrderBy(x => x);
        foreach (var idx in tickIndexes)
            xTicks.Add(new AxisTick { X = X(idx) - 28, Y = CanvasHeight - 18, Label = klines[idx].Date.ToString("yyyy-MM-dd") });
    }

    private static string BuildAgentARequirements(string originalQuestion, string symbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"用户请求: {originalQuestion}");
        sb.AppendLine($"标的代码: {symbol}");
        sb.AppendLine("Agent A 任务拆解：");
        sb.AppendLine("1) Agent B: 收集并分析近一年月K线+近三个月日K线，产出趋势与关键价位。");
        sb.AppendLine("2) Agent C: 收集并分析最近三个月公司新闻与行业新闻，产出事件影响。");
        sb.AppendLine("3) Agent D: 收集并分析最近一年财务序列，产出财务趋势判断。");
        sb.AppendLine("4) Agent A: 汇总 B/C/D 并补充主营业务解析后回复用户。");
        return sb.ToString();
    }

    private string AnalyzeKLineAgentB(string symbol)
    {
        if (DailyKLines.Count < 2 || MonthlyKLines.Count < 2) return "K线数据不足。";
        var dayStart = DailyKLines.First().Close;
        var dayEnd = DailyKLines.Last().Close;
        var dayPct = dayStart == 0 ? 0 : (dayEnd - dayStart) / dayStart * 100m;
        var mStart = MonthlyKLines.First().Close;
        var mEnd = MonthlyKLines.Last().Close;
        var mPct = mStart == 0 ? 0 : (mEnd - mStart) / mStart * 100m;
        var high = DailyKLines.Max(x => x.High);
        var low = DailyKLines.Min(x => x.Low);
        var dHighDate = DailyKLines.OrderByDescending(x => x.High).First().Date;
        var dLowDate = DailyKLines.OrderBy(x => x.Low).First().Date;
        var monthVolTrend = MonthlyKLines.Last().Volume - MonthlyKLines.First().Volume;
        return $"标的 {symbol} K线分析：\n- 近三个月日线涨跌幅：{dayPct:F2}%（起点 {dayStart:F2} -> 终点 {dayEnd:F2}）\n- 近一年月线涨跌幅：{mPct:F2}%（起点 {mStart:F2} -> 终点 {mEnd:F2}）\n- 近三个月区间：高点 {high:F2}（{dHighDate:yyyy-MM-dd}），低点 {low:F2}（{dLowDate:yyyy-MM-dd}）\n- 月线成交量变化：{monthVolTrend:F0}（正值=放量，负值=缩量）\n- 观察：若日线反弹而月线仍弱，短期反弹可持续性需谨慎评估。";
    }

    private string AnalyzeNewsAgentC(string symbol)
    {
        var companyCount = CompanyNewsItems.Count;
        var industryCount = IndustryNewsItems.Count;
        var all = NewsItems.ToList();
        var posWords = new[] { "增长", "回购", "中标", "突破", "增持", "签约" };
        var negWords = new[] { "下滑", "亏损", "减持", "风险", "处罚", "诉讼" };
        int pos = 0, neg = 0;
        foreach (var n in all)
        {
            if (posWords.Any(w => (n.Title + n.Summary).Contains(w))) pos++;
            if (negWords.Any(w => (n.Title + n.Summary).Contains(w))) neg++;
        }
        bool IsPositive(NewsItem n) => n.Sentiment == "positive" || posWords.Any(w => (n.Title + n.Summary).Contains(w));
        bool IsNegative(NewsItem n) => n.Sentiment == "negative" || negWords.Any(w => (n.Title + n.Summary).Contains(w));
        string UrlOrNA(NewsItem n) => string.IsNullOrWhiteSpace(n.Url) ? "N/A" : n.Url;

        var positiveNews = all.Where(IsPositive).OrderByDescending(x => x.PublishTime).Take(8).ToList();
        var negativeNews = all.Where(IsNegative).OrderByDescending(x => x.PublishTime).Take(8).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"标的 {symbol} 新闻分析（最近三个月）：");
        sb.AppendLine($"- 公司新闻：{companyCount} 条；行业新闻：{industryCount} 条");
        sb.AppendLine($"- 事件情绪粗分：偏正面 {pos}，偏负面 {neg}");
        sb.AppendLine("- 积极新闻（可点击 URL）：");
        if (positiveNews.Count == 0) sb.AppendLine("  无");
        else foreach (var n in positiveNews) sb.AppendLine($"  {n.PublishTime:yyyy-MM-dd} | {n.Title} | {UrlOrNA(n)}");
        sb.AppendLine("- 消极新闻（可点击 URL）：");
        if (negativeNews.Count == 0) sb.AppendLine("  无");
        else foreach (var n in negativeNews) sb.AppendLine($"  {n.PublishTime:yyyy-MM-dd} | {n.Title} | {UrlOrNA(n)}");
        sb.AppendLine("- 观察：若行业负面事件增多，即使公司面改善也可能受板块估值压制。");
        return sb.ToString();
    }

    private static string AnalyzeFinancialAgentD(string symbol, List<KeyMetrics> history)
    {
        if (history.Count == 0) return $"标的 {symbol} 最近一年财务数据不足。";
        if (history.Count == 1) return $"标的 {symbol} 仅获取到最近一期财务数据（报告期 {history[0].ReportDate:yyyy-MM-dd}），一年序列不完整。";
        var ordered = history.OrderBy(x => x.ReportDate).ToList();
        var first = ordered.First();
        var last = ordered.Last();
        return $"标的 {symbol} 财务分析（最近一年）：\n- ROE：{first.ROE:F2}% -> {last.ROE:F2}%\n- 毛利率：{first.GrossMargin:F2}% -> {last.GrossMargin:F2}%\n- 净利率：{first.NetMargin:F2}% -> {last.NetMargin:F2}%\n- 资产负债率：{first.DebtRatio:F2}% -> {last.DebtRatio:F2}%\n- 营收增长：{first.RevenueGrowth:F2}% -> {last.RevenueGrowth:F2}%\n- 净利增长：{first.ProfitGrowth:F2}% -> {last.ProfitGrowth:F2}%\n- 观察：关注盈利能力与增长是否同步改善，若背离需警惕质量下滑。";
    }

    private string BuildAgentAFinalPrompt(string userQuestion, string symbol, string mainBusiness, string b, string c, string d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"用户问题: {userQuestion}");
        sb.AppendLine($"标的: {symbol}");
        sb.AppendLine($"主营业务信息: {mainBusiness}");
        sb.AppendLine();
        sb.AppendLine("以下是子代理分析结果：");
        sb.AppendLine($"[Agent B - K线]\n{b}");
        sb.AppendLine($"[Agent C - 新闻]\n{c}");
        sb.AppendLine($"[Agent D - 财务]\n{d}");
        sb.AppendLine();
        sb.AppendLine("你是 Agent A。严格遵守：");
        sb.AppendLine("1) 必须先输出“公司主要业务”。");
        sb.AppendLine("2) 在“公司主要业务”中，先给出行业与板块，再补充一段你自己的业务理解（1段）。");
        sb.AppendLine("3) 必须完整保留并原样输出 Agent B/C/D 的分析结果，不要改写、不要压缩总结。");
        sb.AppendLine("4) 你只额外补充“最终风险提示与投资建议”。");
        sb.AppendLine("5) 最后一行固定输出：⚠️ 以上分析仅供参考");
        return sb.ToString();
    }

    private async Task<string> RunAgentAAsync(string prompt)
    {
        string result = "";
        await foreach (var step in _agent.RunAsync(prompt))
        {
            if (step.Type == AgentStepType.Response) result = step.Content;
        }
        if (!result.Contains("⚠️ 以上分析仅供参考"))
        {
            result = string.IsNullOrWhiteSpace(result)
                ? "⚠️ 以上分析仅供参考"
                : result.TrimEnd() + "\n\n⚠️ 以上分析仅供参考";
        }
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ChartSegment
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public Brush Stroke { get; set; } = Brushes.Gray;
}

public class AxisTick
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = "";
}
