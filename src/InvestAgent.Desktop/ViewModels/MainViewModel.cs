using InvestAgent.Core.Agent;
using InvestAgent.Core.Models;
using InvestAgent.Core.Services;
using InvestAgent.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace InvestAgent.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IAnalysisStreamingObserver
{
    private const double CanvasWidth = 1220;
    private const double CanvasHeight = 420;
    private const double PlotLeft = 78;
    private const double PlotTop = 28;
    private const double PlotRight = 30;
    private const double PlotBottom = 66;

    private readonly IAgentSessionFactory _sessionFactory;
    private readonly ISessionAnalysisOrchestrator _orchestrator;
    private readonly IStockDataService _stockDataService;
    private readonly IAnalysisHistoryRepository _historyRepository;
    private AgentSessionContext? _currentSessionContext;

    private string _userInput = "600519";
    private string _chatInput = "";
    private string _finalResponse = "";
    private string _basicAnalysisResponse = "";
    private string _resolvedSymbol = "";
    private string _metricsSummary = "";
    private string _mainBusiness = "";
    private string _currentSessionLabel = "当前未激活会话";
    private string _currentSessionDisplay = "当前未激活会话";
    private bool _showDailyChart = true;
    private bool _showMonthlyChart = true;
    private HistoryStockGroupViewModel? _selectedHistoryGroup;
    private AnalysisSessionRecord? _selectedHistorySession;

    public ObservableCollection<StepItemViewModel> Steps { get; } = new();
    public ObservableCollection<StockKLine> DailyKLines { get; } = new();
    public ObservableCollection<StockKLine> MonthlyKLines { get; } = new();
    public ObservableCollection<NewsItem> NewsItems { get; } = new();
    public ObservableCollection<NewsItem> CompanyNewsItems { get; } = new();
    public ObservableCollection<NewsItem> IndustryNewsItems { get; } = new();
    public ObservableCollection<KeyMetrics> FinancialHistoryItems { get; } = new();
    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = new();
    public ObservableCollection<HistoryStockGroupViewModel> HistoryGroups { get; } = new();
    public ObservableCollection<AnalysisSessionRecord> VisibleHistorySessions { get; } = new();

    public ObservableCollection<ChartSegment> DailySegments { get; } = new();
    public ObservableCollection<ChartSegment> MonthlySegments { get; } = new();
    public ObservableCollection<ChartPoint> DailyPoints { get; } = new();
    public ObservableCollection<ChartPoint> MonthlyPoints { get; } = new();
    public ObservableCollection<AxisTick> DailyYTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyYTicks { get; } = new();
    public ObservableCollection<AxisTick> DailyXTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyXTicks { get; } = new();

    public ICommand RunCommand { get; }
    public ICommand SendChatCommand { get; }
    public ICommand ToggleDailyViewCommand { get; }
    public ICommand ToggleMonthlyViewCommand { get; }

    public MainViewModel(
        IAgentSessionFactory sessionFactory,
        ISessionAnalysisOrchestrator orchestrator,
        IStockDataService stockDataService,
        IAnalysisHistoryRepository historyRepository)
    {
        _sessionFactory = sessionFactory;
        _orchestrator = orchestrator;
        _stockDataService = stockDataService;
        _historyRepository = historyRepository;

        RunCommand = new RelayCommand(RunAsync, () => !string.IsNullOrWhiteSpace(UserInput));
        SendChatCommand = new RelayCommand(SendChatAsync);
        ToggleDailyViewCommand = new RelayCommand(() => { ShowDailyChart = !ShowDailyChart; return Task.CompletedTask; });
        ToggleMonthlyViewCommand = new RelayCommand(() => { ShowMonthlyChart = !ShowMonthlyChart; return Task.CompletedTask; });

        _ = RefreshHistoryAsync();
    }

    public string UserInput { get => _userInput; set { _userInput = value; OnPropertyChanged(); } }
    public string ChatInput { get => _chatInput; set { _chatInput = value; OnPropertyChanged(); } }
    public string FinalResponse { get => _finalResponse; set { _finalResponse = value; OnPropertyChanged(); } }
    public string BasicAnalysisResponse { get => _basicAnalysisResponse; set { _basicAnalysisResponse = value; OnPropertyChanged(); } }
    public string ResolvedSymbol { get => _resolvedSymbol; set { _resolvedSymbol = value; OnPropertyChanged(); } }
    public string MetricsSummary { get => _metricsSummary; set { _metricsSummary = value; OnPropertyChanged(); } }
    public string MainBusiness { get => _mainBusiness; set { _mainBusiness = value; OnPropertyChanged(); } }
    public string CurrentSessionLabel { get => _currentSessionLabel; set { _currentSessionLabel = value; OnPropertyChanged(); } }
    public string CurrentSessionDisplay { get => _currentSessionDisplay; set { _currentSessionDisplay = value; OnPropertyChanged(); } }
    public bool HasActiveSession => _currentSessionContext is not null;
    public bool ShowDailyChart { get => _showDailyChart; set { _showDailyChart = value; OnPropertyChanged(); } }
    public bool ShowMonthlyChart { get => _showMonthlyChart; set { _showMonthlyChart = value; OnPropertyChanged(); } }
    public HistoryStockGroupViewModel? SelectedHistoryGroup
    {
        get => _selectedHistoryGroup;
        set
        {
            _selectedHistoryGroup = value;
            OnPropertyChanged();
            RefreshVisibleSessions();
        }
    }
    public AnalysisSessionRecord? SelectedHistorySession { get => _selectedHistorySession; set { _selectedHistorySession = value; OnPropertyChanged(); } }

    private async Task RunAsync()
    {
        if (!TryNormalizeTargetInput(UserInput, out var targetInput, out var inputError))
        {
            Steps.Clear();
            Steps.Add(new StepItemViewModel { Title = "输入校验", Content = inputError });
            FinalResponse = inputError;
            return;
        }

        ResetUiData();
        ChatInput = "";

        var symbol = await ResolveSymbolAsync(targetInput);
        var stockName = await ResolveStockNameAsync(symbol, targetInput);
        _currentSessionContext = _sessionFactory.Create(symbol, stockName);
        _currentSessionContext.State.SessionTitle = $"分析会话 {DateTime.Now:yyyy-MM-dd HH:mm}";
        SyncSummaryFields(_currentSessionContext);

        await _orchestrator.RunInitialAnalysisAsync(_currentSessionContext, targetInput, this);
        ApplySessionToUi(_currentSessionContext);
        await SaveCurrentSessionAsync();
        await RefreshHistoryAsync();
    }

    private async Task SendChatAsync()
    {
        if (_currentSessionContext is null || string.IsNullOrWhiteSpace(ChatInput))
            return;

        var message = ChatInput.Trim();
        ChatInput = "";

        await _orchestrator.HandleChatAsync(_currentSessionContext, message, this);
        ApplySessionToUi(_currentSessionContext);
        await SaveCurrentSessionAsync();
        await RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        var groups = await _historyRepository.ListSessionGroupsAsync();
        HistoryGroups.Clear();
        foreach (var group in groups)
        {
            HistoryGroups.Add(new HistoryStockGroupViewModel
            {
                Symbol = group.Symbol,
                StockName = group.StockName,
                Sessions = new ObservableCollection<AnalysisSessionRecord>(group.Sessions)
            });
        }

        if (SelectedHistoryGroup is null && HistoryGroups.Count > 0)
            SelectedHistoryGroup = HistoryGroups[0];
        else
            RefreshVisibleSessions();
    }

    public async Task LoadHistorySessionAsync(AnalysisSessionRecord? session)
    {
        if (session is null)
            return;

        var persisted = await _historyRepository.GetSessionAsync(session.Id);
        if (persisted is null)
            return;

        _currentSessionContext = _sessionFactory.Restore(persisted);
        ApplySessionToUi(_currentSessionContext);
    }

    public async Task DeleteHistorySessionAsync(AnalysisSessionRecord? session)
    {
        if (session is null)
            return;

        await _historyRepository.DeleteSessionAsync(session.Id);

        if (_currentSessionContext?.State.SessionId == session.Id)
        {
            _currentSessionContext = null;
            ResetUiData();
            OnPropertyChanged(nameof(HasActiveSession));
        }

        await RefreshHistoryAsync();
    }

    private async Task SaveCurrentSessionAsync()
    {
        if (_currentSessionContext is null)
            return;

        _currentSessionContext.State.UpdatedAt = DateTime.Now;
        var record = new AnalysisSessionRecord
        {
            Id = _currentSessionContext.State.SessionId,
            Symbol = _currentSessionContext.State.Symbol,
            StockName = _currentSessionContext.State.StockName,
            SessionTitle = _currentSessionContext.State.SessionTitle,
            CreatedAt = _currentSessionContext.State.CreatedAt,
            UpdatedAt = _currentSessionContext.State.UpdatedAt
        };

        var persisted = new PersistedAnalysisSession
        {
            Record = record,
            State = _currentSessionContext.State,
            Messages = _currentSessionContext.Messages,
            WorkflowRuns = _currentSessionContext.WorkflowRuns
        };

        var sessionId = await _historyRepository.SaveSessionAsync(persisted);
        _currentSessionContext.State.SessionId = sessionId;
    }

    private void ApplySessionToUi(AgentSessionContext context)
    {
        SyncSummaryFields(context);

        DailyKLines.Clear();
        MonthlyKLines.Clear();
        NewsItems.Clear();
        CompanyNewsItems.Clear();
        IndustryNewsItems.Clear();
        FinancialHistoryItems.Clear();
        ChatMessages.Clear();

        foreach (var item in context.State.DailyKLines.OrderBy(x => x.Date)) DailyKLines.Add(item);
        foreach (var item in context.State.MonthlyKLines.OrderBy(x => x.Date)) MonthlyKLines.Add(item);
        foreach (var item in context.State.CompanyNews.OrderByDescending(x => x.PublishTime))
        {
            CompanyNewsItems.Add(item);
            NewsItems.Add(item);
        }
        foreach (var item in context.State.IndustryNews.OrderByDescending(x => x.PublishTime))
        {
            IndustryNewsItems.Add(item);
            NewsItems.Add(item);
        }
        foreach (var item in context.State.FinancialHistory.OrderByDescending(x => x.ReportDate))
            FinancialHistoryItems.Add(item);
        foreach (var msg in context.Messages.OrderBy(x => x.CreatedAt))
        {
            ChatMessages.Add(new ChatMessageViewModel
            {
                Source = msg,
                Role = msg.Role,
                Content = msg.Content,
                Timestamp = msg.CreatedAt
            });
        }

        Steps.Clear();
        foreach (var run in context.WorkflowRuns.OrderBy(x => x.CreatedAt))
        {
            foreach (var step in run.Steps)
            {
                Steps.Add(new StepItemViewModel
                {
                    Title = $"{run.AgentName} | {BuildStepTitle(step)}",
                    Content = step.FunctionResult is { Length: > 0 }
                        ? $"{step.Content}\n{step.FunctionResult}"
                        : step.Content
                });
            }
        }

        BuildChart(DailyKLines.ToList(), DailySegments, DailyPoints, DailyYTicks, DailyXTicks);
        BuildChart(MonthlyKLines.ToList(), MonthlySegments, MonthlyPoints, MonthlyYTicks, MonthlyXTicks);
        OnPropertyChanged(nameof(HasActiveSession));
    }

    public async Task OnStepAddedAsync(AgentSessionContext context, string agentName, AgentStep step, int turnIndex)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Steps.Add(new StepItemViewModel
            {
                Title = $"{agentName} | {BuildStepTitle(step)}",
                Content = step.FunctionResult is { Length: > 0 }
                    ? $"{step.Content}\n{step.FunctionResult}"
                    : step.Content
            });
        });
    }

    public async Task OnStatePatchedAsync(AgentSessionContext context, SessionStatePatch patch)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SyncSummaryFields(context);

            if (patch.DailyKLines is not null)
            {
                SyncKLines(DailyKLines, patch.DailyKLines);
                BuildChart(DailyKLines.ToList(), DailySegments, DailyPoints, DailyYTicks, DailyXTicks);
            }

            if (patch.MonthlyKLines is not null)
            {
                SyncKLines(MonthlyKLines, patch.MonthlyKLines);
                BuildChart(MonthlyKLines.ToList(), MonthlySegments, MonthlyPoints, MonthlyYTicks, MonthlyXTicks);
            }

            if (patch.CompanyNews is not null || patch.IndustryNews is not null)
                SyncNewsCollections(context.State);

            if (patch.FinancialHistory is not null)
            {
                FinancialHistoryItems.Clear();
                foreach (var item in context.State.FinancialHistory.OrderByDescending(x => x.ReportDate))
                    FinancialHistoryItems.Add(item);
                MetricsSummary = BuildFinancialSummary(context.State.FinancialHistory);
            }
        });
    }

    public async Task OnMessageAddedAsync(AgentSessionContext context, SessionChatMessage message)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (ChatMessages.Any(x => ReferenceEquals(x.Source, message)))
                return;

            ChatMessages.Add(new ChatMessageViewModel
            {
                Source = message,
                Role = message.Role,
                Content = message.Content,
                Timestamp = message.CreatedAt
            });
        });
    }

    public async Task OnMessageUpdatedAsync(AgentSessionContext context, SessionChatMessage message)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var existing = ChatMessages.FirstOrDefault(x => ReferenceEquals(x.Source, message));
            if (existing is null)
            {
                ChatMessages.Add(new ChatMessageViewModel
                {
                    Source = message,
                    Role = message.Role,
                    Content = message.Content,
                    Timestamp = message.CreatedAt
                });
                return;
            }

            existing.Content = message.Content;
            existing.Timestamp = message.CreatedAt;
        });
    }

    private void RefreshVisibleSessions()
    {
        VisibleHistorySessions.Clear();
        if (SelectedHistoryGroup is null)
            return;

        foreach (var session in SelectedHistoryGroup.Sessions.OrderByDescending(x => x.UpdatedAt))
            VisibleHistorySessions.Add(session);

        if (VisibleHistorySessions.Count > 0 && (SelectedHistorySession is null || !VisibleHistorySessions.Any(x => x.Id == SelectedHistorySession.Id)))
            SelectedHistorySession = VisibleHistorySessions[0];
    }

    private void ResetUiData()
    {
        Steps.Clear();
        DailyKLines.Clear();
        MonthlyKLines.Clear();
        NewsItems.Clear();
        CompanyNewsItems.Clear();
        IndustryNewsItems.Clear();
        FinancialHistoryItems.Clear();
        ChatMessages.Clear();
        DailySegments.Clear();
        MonthlySegments.Clear();
        DailyPoints.Clear();
        MonthlyPoints.Clear();
        DailyYTicks.Clear();
        MonthlyYTicks.Clear();
        DailyXTicks.Clear();
        MonthlyXTicks.Clear();
        FinalResponse = "";
        BasicAnalysisResponse = "";
        MetricsSummary = "";
        MainBusiness = "";
        CurrentSessionLabel = "当前未激活会话";
        CurrentSessionDisplay = "当前未激活会话";
    }

    private void SyncSummaryFields(AgentSessionContext context)
    {
        ResolvedSymbol = context.State.Symbol;
        MainBusiness = context.State.MainBusiness;
        FinalResponse = context.State.FinalResponse;
        BasicAnalysisResponse = string.IsNullOrWhiteSpace(context.State.InitialAnalysisResponse)
            ? BuildLiveBasicAnalysisPreview(context.State)
            : context.State.InitialAnalysisResponse;
        MetricsSummary = BuildFinancialSummary(context.State.FinancialHistory);
        CurrentSessionLabel = $"{context.State.Symbol} {context.State.StockName} | {context.State.SessionTitle}";
        CurrentSessionDisplay = $"{context.State.Symbol} {context.State.StockName} |\n{context.State.SessionTitle}";
        OnPropertyChanged(nameof(HasActiveSession));
    }

    private void SyncKLines(ObservableCollection<StockKLine> target, List<StockKLine> source)
    {
        target.Clear();
        foreach (var item in source.OrderBy(x => x.Date))
            target.Add(item);
    }

    private void SyncNewsCollections(AnalysisSessionState state)
    {
        NewsItems.Clear();
        CompanyNewsItems.Clear();
        IndustryNewsItems.Clear();

        foreach (var item in state.CompanyNews.OrderByDescending(x => x.PublishTime))
        {
            CompanyNewsItems.Add(item);
            NewsItems.Add(item);
        }

        foreach (var item in state.IndustryNews.OrderByDescending(x => x.PublishTime))
        {
            IndustryNewsItems.Add(item);
            NewsItems.Add(item);
        }
    }

    private static string BuildLiveBasicAnalysisPreview(AnalysisSessionState state)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(state.MainBusiness))
        {
            sb.AppendLine("## 公司主要业务");
            sb.AppendLine(state.MainBusiness);
        }
        if (!string.IsNullOrWhiteSpace(state.AgentBResult))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("## K线分析");
            sb.AppendLine(state.AgentBResult);
        }
        if (!string.IsNullOrWhiteSpace(state.AgentCResult))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("## 新闻分析");
            sb.AppendLine(state.AgentCResult);
        }
        if (!string.IsNullOrWhiteSpace(state.AgentDResult))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("## 财务分析");
            sb.AppendLine(state.AgentDResult);
        }
        if (!string.IsNullOrWhiteSpace(state.FinalRiskAdvice))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("## 最终风险提示与投资建议");
            sb.AppendLine(state.FinalRiskAdvice);
            sb.AppendLine();
            sb.AppendLine("⚠️ 以上分析仅供参考");
        }

        return sb.Length == 0 ? "正在准备基础分析，请稍候..." : sb.ToString().Trim();
    }

    private async Task<string> ResolveSymbolAsync(string input)
    {
        var code = Regex.Match(input, @"\b\d{6}\b").Value;
        if (!string.IsNullOrWhiteSpace(code)) return code;
        var letters = Regex.Match(input, @"\b[A-Za-z]{1,10}\b").Value;
        if (!string.IsNullOrWhiteSpace(letters)) return letters.ToUpperInvariant();

        var results = await _stockDataService.SearchStockAsync(input);
        var picked = results.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Symbol));
        return picked?.Symbol ?? "600519";
    }

    private async Task<string> ResolveStockNameAsync(string symbol, string input)
    {
        var metrics = await SafeLoadAsync(() => _stockDataService.GetKeyMetricsAsync(symbol), null as KeyMetrics);
        if (!string.IsNullOrWhiteSpace(metrics?.Name))
            return metrics.Name;
        return Regex.IsMatch(input, @"^[\u4e00-\u9fa5]{2,20}$") ? input : symbol;
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

        error = "输入格式仅支持“股票代码”或“股票名称”，聊天区才支持自然语言追问。";
        return false;
    }

    private static string BuildFinancialSummary(List<KeyMetrics> history)
    {
        if (history.Count == 0) return "财务数据不可用。";
        var latest = history.OrderByDescending(x => x.ReportDate).First();
        var sb = new StringBuilder();
        sb.AppendLine($"名称: {latest.Name}");
        sb.AppendLine($"ROE: {latest.ROE:F2}%  ROA: {latest.ROA:F2}%");
        sb.AppendLine($"毛利率: {latest.GrossMargin:F2}%  净利率: {latest.NetMargin:F2}%");
        sb.AppendLine($"营收增长: {latest.RevenueGrowth:F2}%  净利增长: {latest.ProfitGrowth:F2}%  资产负债率: {latest.DebtRatio:F2}%");
        sb.AppendLine($"报告期: {latest.ReportDate:yyyy-MM-dd}");
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("最近财务序列:");
            foreach (var item in history.OrderByDescending(x => x.ReportDate))
                sb.AppendLine($"{item.ReportDate:yyyy-MM-dd} | ROE:{item.ROE:F2}% 毛利率:{item.GrossMargin:F2}% 净利率:{item.NetMargin:F2}% 营收增长:{item.RevenueGrowth:F2}% 净利增长:{item.ProfitGrowth:F2}% 负债率:{item.DebtRatio:F2}%");
        }
        return sb.ToString();
    }

    private static async Task<T> SafeLoadAsync<T>(Func<Task<T>> loader, T fallback)
    {
        try { return await loader(); } catch { return fallback; }
    }

    private static Brush GetRiseFallBrush(decimal prev, decimal curr) => curr >= prev ? Brushes.Red : Brushes.Green;

    private static void BuildChart(
        List<StockKLine> klines,
        ObservableCollection<ChartSegment> segments,
        ObservableCollection<ChartPoint> points,
        ObservableCollection<AxisTick> yTicks,
        ObservableCollection<AxisTick> xTicks)
    {
        segments.Clear();
        points.Clear();
        yTicks.Clear();
        xTicks.Clear();
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
            segments.Add(new ChartSegment
            {
                X1 = X(i - 1),
                Y1 = Y(prev),
                X2 = X(i),
                Y2 = Y(curr),
                Stroke = GetRiseFallBrush(prev, curr)
            });
        }

        for (int i = 0; i < klines.Count; i++)
        {
            var current = klines[i];
            var prev = i == 0 ? current.Close : klines[i - 1].Close;
            var changePct = prev == 0 ? 0 : (current.Close - prev) / prev * 100;
            points.Add(new ChartPoint
            {
                X = X(i) - 12,
                Y = Y(current.Close) - 12,
                Diameter = 24,
                Fill = GetRiseFallBrush(prev, current.Close),
                ToolTip = $"时间: {current.Date:yyyy-MM-dd}\n价格: {current.Close:F2}\n涨跌幅: {changePct:+0.00;-0.00;0.00}%"
            });
        }

        for (int t = 0; t <= 4; t++)
        {
            var v = max - t * span / 4;
            yTicks.Add(new AxisTick { X = 8, Y = PlotTop + t * plotH / 4 - 10, Label = v.ToString("F2") });
        }

        var tickIndexes = new[] { 0, klines.Count / 3, (klines.Count * 2) / 3, klines.Count - 1 }.Distinct().OrderBy(x => x);
        foreach (var idx in tickIndexes)
            xTicks.Add(new AxisTick { X = X(idx) - 34, Y = CanvasHeight - 28, Label = klines[idx].Date.ToString("yyyy-MM-dd") });
    }

    private static string BuildStepTitle(AgentStep step)
    {
        return step.Type switch
        {
            AgentStepType.Thought => "Thought",
            AgentStepType.Action => $"Action | {step.FunctionName}",
            AgentStepType.Observation => $"Observation | {step.FunctionName}",
            AgentStepType.Response => "Response",
            _ => step.Type.ToString()
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _role = "";
    private string _content = "";
    private DateTime _timestamp;

    public SessionChatMessage? Source { get; set; }

    public string Role { get => _role; set { _role = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUser)); OnPropertyChanged(nameof(RoleLabel)); } }
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public string RoleLabel => IsUser ? "用户" : "Agent";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class HistoryStockGroupViewModel
{
    public string Symbol { get; set; } = "";
    public string StockName { get; set; } = "";
    public ObservableCollection<AnalysisSessionRecord> Sessions { get; set; } = new();
    public string DisplayName => $"{Symbol} {StockName}";
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

public class ChartPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Diameter { get; set; } = 14;
    public Brush Fill { get; set; } = Brushes.SteelBlue;
    public string ToolTip { get; set; } = "";
}
