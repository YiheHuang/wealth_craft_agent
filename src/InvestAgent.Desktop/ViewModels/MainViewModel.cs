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

/// <summary>
/// 桌面应用的主视图模型（MVVM 核心）。
/// 负责分析流程控制、K线图交互（缩放/平移/Hover）、状态同步（实现 IAnalysisStreamingObserver）和历史会话管理。
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IAnalysisStreamingObserver
{
    private const double CanvasWidth = 1220;
    private const double CanvasHeight = 420;
    private const double PlotLeft = 78;
    private const double PlotTop = 28;
    private const double PlotRight = 30;
    private const double PlotBottom = 66;
    private const int DailyChartCacheDays = 1200;
    private const int MonthlyChartCacheMonths = 180;

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
    private string _statusText = "Ready";
    private int _dailyDaysInput = 90;
    private int _monthlyMonthsInput = 12;
    private string _dailyDaysInputText = "90";
    private string _monthlyMonthsInputText = "12";
    private bool _syncingRangeText;
    private bool _suppressChartRangeUpdates;
    private int _dailyChartStartIndex;
    private int _dailyChartVisibleCount;
    private int _monthlyChartStartIndex;
    private int _monthlyChartVisibleCount;
    private string _dailyChartWindowLabel = "显示全部";
    private string _monthlyChartWindowLabel = "显示全部";
    private bool _isAnalyzing;
    private bool _showDailyChart = true;
    private bool _showMonthlyChart = true;
    private HistoryStockGroupViewModel? _selectedHistoryGroup;
    private AnalysisSessionRecord? _selectedHistorySession;
    private List<StockKLine> _dailyKLineCache = new();
    private List<StockKLine> _monthlyKLineCache = new();

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

    public ObservableCollection<CandleItem> DailyCandles { get; } = new();
    public ObservableCollection<CandleItem> MonthlyCandles { get; } = new();
    public ObservableCollection<AxisTick> DailyYTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyYTicks { get; } = new();
    public ObservableCollection<AxisTick> DailyXTicks { get; } = new();
    public ObservableCollection<AxisTick> MonthlyXTicks { get; } = new();
    public ChartHoverInfo DailyChartHover { get; } = new();
    public ChartHoverInfo MonthlyChartHover { get; } = new();

    public ICommand RunCommand { get; }
    public ICommand SendChatCommand { get; }
    public ICommand ToggleDailyViewCommand { get; }
    public ICommand ToggleMonthlyViewCommand { get; }
    public ICommand ResetDailyChartCommand { get; }
    public ICommand ResetMonthlyChartCommand { get; }
    public ICommand SetDailyRangeCommand { get; }
    public ICommand SetMonthlyRangeCommand { get; }
    public ICommand ReanalyzeCurrentChartRangeCommand { get; }

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

        RunCommand = new RelayCommand(RunAsync, () => !IsAnalyzing && !string.IsNullOrWhiteSpace(UserInput));
        SendChatCommand = new RelayCommand(SendChatAsync, () => !IsAnalyzing && HasActiveSession && !string.IsNullOrWhiteSpace(ChatInput));
        ToggleDailyViewCommand = new RelayCommand(() => { ShowDailyChart = !ShowDailyChart; return Task.CompletedTask; });
        ToggleMonthlyViewCommand = new RelayCommand(() => { ShowMonthlyChart = !ShowMonthlyChart; return Task.CompletedTask; });
        ResetDailyChartCommand = new RelayCommand(() => { ResetChartWindow(true); return Task.CompletedTask; });
        ResetMonthlyChartCommand = new RelayCommand(() => { ResetChartWindow(false); return Task.CompletedTask; });
        SetDailyRangeCommand = new RelayCommand(parameter => SetDailyRangeAsync(parameter));
        SetMonthlyRangeCommand = new RelayCommand(parameter => SetMonthlyRangeAsync(parameter));
        ReanalyzeCurrentChartRangeCommand = new RelayCommand(ReanalyzeCurrentChartRangeAsync, () => !IsAnalyzing && HasActiveSession && IsKLineRangeNoticeVisible);

        _ = RefreshHistoryAsync();
    }

    public string UserInput { get => _userInput; set { if (SetProperty(ref _userInput, value)) RaiseCommandStates(); } }
    public string ChatInput { get => _chatInput; set { if (SetProperty(ref _chatInput, value)) RaiseCommandStates(); } }
    public string FinalResponse { get => _finalResponse; set => SetProperty(ref _finalResponse, value); }
    public string BasicAnalysisResponse { get => _basicAnalysisResponse; set => SetProperty(ref _basicAnalysisResponse, value); }
    public string ResolvedSymbol { get => _resolvedSymbol; set => SetProperty(ref _resolvedSymbol, value); }
    public string MetricsSummary { get => _metricsSummary; set => SetProperty(ref _metricsSummary, value); }
    public string MainBusiness { get => _mainBusiness; set => SetProperty(ref _mainBusiness, value); }
    public string CurrentSessionLabel { get => _currentSessionLabel; set => SetProperty(ref _currentSessionLabel, value); }
    public string CurrentSessionDisplay { get => _currentSessionDisplay; set => SetProperty(ref _currentSessionDisplay, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string DailyDaysInputText
    {
        get => _dailyDaysInputText;
        set
        {
            if (SetProperty(ref _dailyDaysInputText, value) && !_syncingRangeText)
            {
                var normalizedText = (value ?? "").Trim();
                if (int.TryParse(normalizedText, out var days) && days is >= 5 and <= 1200)
                    DailyDaysInput = days;
            }
        }
    }
    public string MonthlyMonthsInputText
    {
        get => _monthlyMonthsInputText;
        set
        {
            if (SetProperty(ref _monthlyMonthsInputText, value) && !_syncingRangeText)
            {
                var normalizedText = (value ?? "").Trim();
                if (int.TryParse(normalizedText, out var months) && months is >= 1 and <= 180)
                    MonthlyMonthsInput = months;
            }
        }
    }
    public int DailyDaysInput
    {
        get => _dailyDaysInput;
        set
        {
            if (SetProperty(ref _dailyDaysInput, NormalizeDailyDays(value)))
            {
                SyncDailyDaysInputText();
                OnPropertyChanged(nameof(DailyTabTitle));
                OnPropertyChanged(nameof(DailyChartTitle));
                RaiseKLineRangeProperties();
                if (!_suppressChartRangeUpdates)
                    _ = ApplyDisplayRangeChangeAsync(true);
            }
        }
    }
    public int MonthlyMonthsInput
    {
        get => _monthlyMonthsInput;
        set
        {
            if (SetProperty(ref _monthlyMonthsInput, NormalizeMonthlyMonths(value)))
            {
                SyncMonthlyMonthsInputText();
                OnPropertyChanged(nameof(MonthlyTabTitle));
                OnPropertyChanged(nameof(MonthlyChartTitle));
                RaiseKLineRangeProperties();
                if (!_suppressChartRangeUpdates)
                    _ = ApplyDisplayRangeChangeAsync(false);
            }
        }
    }
    public string DailyTabTitle => $"{DailyDaysInput}日K线";
    public string MonthlyTabTitle => $"{MonthlyMonthsInput}个月月K线";
    public string DailyChartTitle => $"近 {DailyDaysInput} 个交易日K线完整数据";
    public string MonthlyChartTitle => $"近 {MonthlyMonthsInput} 个月月K线完整数据";
    public string KLineAnalysisRangeLabel => _currentSessionContext is null
        ? ""
        : $"AI结论基于：{_currentSessionContext.State.DailyDays}日K / {_currentSessionContext.State.MonthlyMonths}个月月K";
    public string KLineRangeNotice => _currentSessionContext is null
        ? ""
        : $"当前图表范围已变更，AI结论仍基于 {_currentSessionContext.State.DailyDays}日K / {_currentSessionContext.State.MonthlyMonths}个月月K。";
    public bool IsKLineRangeNoticeVisible => _currentSessionContext is not null &&
                                             (DailyDaysInput != _currentSessionContext.State.DailyDays ||
                                              MonthlyMonthsInput != _currentSessionContext.State.MonthlyMonths);
    public string DailyChartWindowLabel { get => _dailyChartWindowLabel; private set => SetProperty(ref _dailyChartWindowLabel, value); }
    public string MonthlyChartWindowLabel { get => _monthlyChartWindowLabel; private set => SetProperty(ref _monthlyChartWindowLabel, value); }
    public bool IsAnalyzing { get => _isAnalyzing; private set { if (SetProperty(ref _isAnalyzing, value)) RaiseCommandStates(); } }
    public bool HasActiveSession => _currentSessionContext is not null;
    public bool ShowDailyChart { get => _showDailyChart; set => SetProperty(ref _showDailyChart, value); }
    public bool ShowMonthlyChart { get => _showMonthlyChart; set => SetProperty(ref _showMonthlyChart, value); }
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
            StatusText = "Input invalid";
            return;
        }

        IsAnalyzing = true;
        StatusText = "Preparing analysis...";
        try
        {
            ResetUiData();
            ChatInput = "";

            StatusText = "Resolving symbol...";
            var symbol = await ResolveSymbolAsync(targetInput);
            var stockName = await ResolveStockNameAsync(symbol, targetInput);
            _currentSessionContext = _sessionFactory.Create(symbol, stockName);
            _currentSessionContext.State.SessionTitle = $"分析会话 {DateTime.Now:yyyy-MM-dd HH:mm}";
            _currentSessionContext.State.DailyDays = NormalizeDailyDays(DailyDaysInput);
            _currentSessionContext.State.MonthlyMonths = NormalizeMonthlyMonths(MonthlyMonthsInput);
            DailyDaysInput = _currentSessionContext.State.DailyDays;
            MonthlyMonthsInput = _currentSessionContext.State.MonthlyMonths;
            SyncSummaryFields(_currentSessionContext);

            StatusText = "Generating analysis...";
            await _orchestrator.RunInitialAnalysisAsync(_currentSessionContext, targetInput, this);
            ApplySessionToUi(_currentSessionContext);
            await SaveCurrentSessionAsync();
            await RefreshHistoryAsync();
            StatusText = "Done";
        }
        catch
        {
            StatusText = "Failed";
            throw;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private async Task SendChatAsync()
    {
        if (_currentSessionContext is null || string.IsNullOrWhiteSpace(ChatInput))
            return;

        IsAnalyzing = true;
        StatusText = "Generating answer...";
        try
        {
            var message = ChatInput.Trim();
            ChatInput = "";

            await _orchestrator.HandleChatAsync(_currentSessionContext, message, this);
            ApplySessionToUi(_currentSessionContext);
            await SaveCurrentSessionAsync();
            await RefreshHistoryAsync();
            StatusText = "Done";
        }
        catch
        {
            StatusText = "Failed";
            throw;
        }
        finally
        {
            IsAnalyzing = false;
        }
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

        _dailyKLineCache = context.State.DailyKLines.OrderBy(x => x.Date).ToList();
        _monthlyKLineCache = context.State.MonthlyKLines.OrderBy(x => x.Date).ToList();
        SetDisplayRangeToAnalysisSnapshot(context.State);
        ApplyKLineDisplayRange(true);
        ApplyKLineDisplayRange(false);
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

        OnPropertyChanged(nameof(HasActiveSession));
        RaiseKLineRangeProperties();
    }

    public async Task OnStepAddedAsync(AgentSessionContext context, string agentName, AgentStep step, int turnIndex)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusText = $"{agentName}: {BuildStepTitle(step)}";
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

            if (patch.DailyDays.HasValue || patch.MonthlyMonths.HasValue)
                SetDisplayRangeToAnalysisSnapshot(context.State);

            if (patch.DailyKLines is not null)
            {
                _dailyKLineCache = patch.DailyKLines.OrderBy(x => x.Date).ToList();
                ApplyKLineDisplayRange(true);
            }

            if (patch.MonthlyKLines is not null)
            {
                _monthlyKLineCache = patch.MonthlyKLines.OrderBy(x => x.Date).ToList();
                ApplyKLineDisplayRange(false);
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
        _dailyKLineCache.Clear();
        _monthlyKLineCache.Clear();
        DailyCandles.Clear();
        MonthlyCandles.Clear();
        DailyChartHover.Hide();
        MonthlyChartHover.Hide();
        _dailyChartStartIndex = 0;
        _dailyChartVisibleCount = 0;
        _monthlyChartStartIndex = 0;
        _monthlyChartVisibleCount = 0;
        DailyChartWindowLabel = "显示全部";
        MonthlyChartWindowLabel = "显示全部";
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
        RaiseKLineRangeProperties();
        RaiseCommandStates();
    }

    private void SetDisplayRangeToAnalysisSnapshot(AnalysisSessionState state)
    {
        _suppressChartRangeUpdates = true;
        try
        {
            DailyDaysInput = NormalizeDailyDays(state.DailyDays);
            MonthlyMonthsInput = NormalizeMonthlyMonths(state.MonthlyMonths);
        }
        finally
        {
            _suppressChartRangeUpdates = false;
        }

        RaiseKLineRangeProperties();
    }

    private async Task SetDailyRangeAsync(object? parameter)
    {
        if (TryReadRangeParameter(parameter, out var value))
            DailyDaysInput = NormalizeDailyDays(value);
        await Task.CompletedTask;
    }

    private async Task SetMonthlyRangeAsync(object? parameter)
    {
        if (TryReadRangeParameter(parameter, out var value))
            MonthlyMonthsInput = NormalizeMonthlyMonths(value);
        await Task.CompletedTask;
    }

    private async Task ReanalyzeCurrentChartRangeAsync()
    {
        if (_currentSessionContext is null)
            return;

        IsAnalyzing = true;
        StatusText = "Reanalyzing K-line range...";
        try
        {
            var dailyDays = NormalizeDailyDays(DailyDaysInput);
            var monthlyMonths = NormalizeMonthlyMonths(MonthlyMonthsInput);
            var message = $"按当前图表显示范围重新进行K线分析：日K {dailyDays} 个交易日，月K {monthlyMonths} 个月。只更新K线/技术结构判断，并说明本次分析窗口。";
            await _orchestrator.HandleChatAsync(_currentSessionContext, message, this);
            ApplySessionToUi(_currentSessionContext);
            await SaveCurrentSessionAsync();
            await RefreshHistoryAsync();
            StatusText = "Done";
        }
        catch
        {
            StatusText = "Failed";
            throw;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private async Task ApplyDisplayRangeChangeAsync(bool isDaily)
    {
        try
        {
            await EnsureKLineCacheForDisplayAsync(isDaily);
            ApplyKLineDisplayRange(isDaily);
            RaiseKLineRangeProperties();
        }
        catch
        {
            StatusText = isDaily ? "Daily chart range update failed" : "Monthly chart range update failed";
        }
    }

    private async Task EnsureKLineCacheForDisplayAsync(bool isDaily)
    {
        if (_currentSessionContext is null)
            return;

        var requested = isDaily ? NormalizeDailyDays(DailyDaysInput) : NormalizeMonthlyMonths(MonthlyMonthsInput);
        var cache = isDaily ? _dailyKLineCache : _monthlyKLineCache;
        if (cache.Count >= requested)
            return;

        var symbol = _currentSessionContext.State.Symbol;
        var loadCount = isDaily
            ? Math.Max(requested, DailyChartCacheDays)
            : Math.Max(requested, MonthlyChartCacheMonths);
        var loaded = isDaily
            ? await SafeLoadAsync(() => _stockDataService.GetHistoricalPricesAsync(symbol, loadCount), new List<StockKLine>())
            : await SafeLoadAsync(() => _stockDataService.GetMonthlyKLineAsync(symbol, loadCount), new List<StockKLine>());

        if (loaded.Count == 0)
            return;

        var ordered = loaded.OrderBy(x => x.Date).ToList();
        if (isDaily)
        {
            _dailyKLineCache = ordered;
            _currentSessionContext.State.DailyKLines = ordered;
        }
        else
        {
            _monthlyKLineCache = ordered;
            _currentSessionContext.State.MonthlyKLines = ordered;
        }
    }

    private void ApplyKLineDisplayRange(bool isDaily)
    {
        var source = isDaily ? _dailyKLineCache : _monthlyKLineCache;
        var target = isDaily ? DailyKLines : MonthlyKLines;
        var count = isDaily ? NormalizeDailyDays(DailyDaysInput) : NormalizeMonthlyMonths(MonthlyMonthsInput);
        SyncKLines(target, source.TakeLast(Math.Max(1, count)).ToList());
        ResetChartWindow(isDaily);
    }

    private void SyncKLines(ObservableCollection<StockKLine> target, List<StockKLine> source)
    {
        target.Clear();
        foreach (var item in source.OrderBy(x => x.Date))
            target.Add(item);
    }

    private void RaiseKLineRangeProperties()
    {
        OnPropertyChanged(nameof(KLineAnalysisRangeLabel));
        OnPropertyChanged(nameof(KLineRangeNotice));
        OnPropertyChanged(nameof(IsKLineRangeNoticeVisible));
        RaiseCommandStates();
    }

    private void SyncDailyDaysInputText()
    {
        _syncingRangeText = true;
        try
        {
            DailyDaysInputText = DailyDaysInput.ToString();
        }
        finally
        {
            _syncingRangeText = false;
        }
    }

    private void SyncMonthlyMonthsInputText()
    {
        _syncingRangeText = true;
        try
        {
            MonthlyMonthsInputText = MonthlyMonthsInput.ToString();
        }
        finally
        {
            _syncingRangeText = false;
        }
    }

    private static bool TryReadRangeParameter(object? parameter, out int value)
    {
        value = 0;
        return parameter is not null && int.TryParse(parameter.ToString(), out value);
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
        var metricsName = CleanStockName(metrics?.Name, symbol);
        if (!string.IsNullOrWhiteSpace(metricsName))
            return metricsName;

        var searchResults = await SafeLoadAsync(() => _stockDataService.SearchStockAsync(symbol), new List<StockQuote>());
        var matched = searchResults.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var searchedName = CleanStockName(matched?.Name, symbol);
        if (!string.IsNullOrWhiteSpace(searchedName))
            return searchedName;

        if (!string.Equals(input, symbol, StringComparison.OrdinalIgnoreCase))
        {
            var inputName = CleanStockName(input, symbol);
            if (!string.IsNullOrWhiteSpace(inputName) && Regex.IsMatch(inputName, @"^[\u4e00-\u9fa5]{2,20}$"))
                return inputName;
        }

        return Regex.IsMatch(input, @"^[\u4e00-\u9fa5]{2,20}$") ? input : symbol;
    }

    private static string CleanStockName(string? name, string symbol)
    {
        var cleaned = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "";

        cleaned = Regex.Replace(cleaned, @"\s*[（(][^）)]*[）)]\s*$", "").Trim();
        if (string.Equals(cleaned, symbol, StringComparison.OrdinalIgnoreCase))
            return "";
        if (Regex.IsMatch(cleaned, @"^\d{6}$"))
            return "";
        return cleaned;
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

    public void ZoomDailyChartAt(double canvasX, int wheelDelta) => ZoomChartAt(true, canvasX, wheelDelta > 0);

    public void ZoomMonthlyChartAt(double canvasX, int wheelDelta) => ZoomChartAt(false, canvasX, wheelDelta > 0);

    public bool PanDailyChartByPixels(double deltaX) => PanChartByPixels(true, deltaX);

    public bool PanMonthlyChartByPixels(double deltaX) => PanChartByPixels(false, deltaX);

    public void ResetDailyChart() => ResetChartWindow(true);

    public void ResetMonthlyChart() => ResetChartWindow(false);

    public void ShowDailyChartHover(double canvasX, double canvasY) => ShowChartHover(true, canvasX, canvasY);

    public void ShowMonthlyChartHover(double canvasX, double canvasY) => ShowChartHover(false, canvasX, canvasY);

    public void HideDailyChartHover() => DailyChartHover.Hide();

    public void HideMonthlyChartHover() => MonthlyChartHover.Hide();

    private void ZoomChartAt(bool isDaily, double canvasX, bool zoomIn)
    {
        var sourceCount = isDaily ? DailyKLines.Count : MonthlyKLines.Count;
        if (sourceCount == 0)
            return;

        ref var start = ref (isDaily ? ref _dailyChartStartIndex : ref _monthlyChartStartIndex);
        ref var visibleCount = ref (isDaily ? ref _dailyChartVisibleCount : ref _monthlyChartVisibleCount);
        EnsureChartWindow(sourceCount, ref start, ref visibleCount);

        var current = visibleCount == 0 ? sourceCount : visibleCount;
        var plotW = CanvasWidth - PlotLeft - PlotRight;
        var anchorRatio = Math.Clamp((canvasX - PlotLeft) / plotW, 0, 1);
        var anchorIndex = start + anchorRatio * current;
        var next = zoomIn
            ? Math.Max(MinVisibleCandles(sourceCount), (int)Math.Round(current * 0.72))
            : Math.Min(sourceCount, (int)Math.Ceiling(current / 0.72));

        visibleCount = next >= sourceCount ? 0 : next;
        start = visibleCount == 0 ? 0 : (int)Math.Round(anchorIndex - anchorRatio * visibleCount);
        EnsureChartWindow(sourceCount, ref start, ref visibleCount);
        RebuildChart(isDaily);
    }

    private bool PanChartByPixels(bool isDaily, double deltaX)
    {
        var sourceCount = isDaily ? DailyKLines.Count : MonthlyKLines.Count;
        if (sourceCount == 0)
            return false;

        ref var start = ref (isDaily ? ref _dailyChartStartIndex : ref _monthlyChartStartIndex);
        ref var visibleCount = ref (isDaily ? ref _dailyChartVisibleCount : ref _monthlyChartVisibleCount);
        EnsureChartWindow(sourceCount, ref start, ref visibleCount);
        if (visibleCount == 0 || visibleCount >= sourceCount)
            return false;

        var plotW = CanvasWidth - PlotLeft - PlotRight;
        var slotW = plotW / visibleCount;
        var bars = (int)Math.Round(-deltaX / slotW);
        return bars != 0 && PanChartByBars(isDaily, bars);
    }

    private bool PanChartByBars(bool isDaily, int bars)
    {
        var sourceCount = isDaily ? DailyKLines.Count : MonthlyKLines.Count;
        if (sourceCount == 0 || bars == 0)
            return false;

        ref var start = ref (isDaily ? ref _dailyChartStartIndex : ref _monthlyChartStartIndex);
        ref var visibleCount = ref (isDaily ? ref _dailyChartVisibleCount : ref _monthlyChartVisibleCount);
        EnsureChartWindow(sourceCount, ref start, ref visibleCount);
        if (visibleCount == 0 || visibleCount >= sourceCount)
            return false;

        var oldStart = start;
        start += bars;
        EnsureChartWindow(sourceCount, ref start, ref visibleCount);
        if (start == oldStart)
            return false;

        RebuildChart(isDaily);
        return true;
    }

    private void ResetChartWindow(bool isDaily)
    {
        if (isDaily)
        {
            _dailyChartStartIndex = 0;
            _dailyChartVisibleCount = 0;
        }
        else
        {
            _monthlyChartStartIndex = 0;
            _monthlyChartVisibleCount = 0;
        }

        RebuildChart(isDaily);
    }

    private void RebuildChart(bool isDaily)
    {
        if (isDaily)
        {
            var visible = GetVisibleKLines(DailyKLines.ToList(), ref _dailyChartStartIndex, ref _dailyChartVisibleCount);
            BuildChart(visible, DailyCandles, DailyYTicks, DailyXTicks);
            DailyChartWindowLabel = BuildChartWindowLabel(visible, DailyKLines.Count);
            DailyChartHover.Hide();
        }
        else
        {
            var visible = GetVisibleKLines(MonthlyKLines.ToList(), ref _monthlyChartStartIndex, ref _monthlyChartVisibleCount);
            BuildChart(visible, MonthlyCandles, MonthlyYTicks, MonthlyXTicks);
            MonthlyChartWindowLabel = BuildChartWindowLabel(visible, MonthlyKLines.Count);
            MonthlyChartHover.Hide();
        }
    }

    private void ShowChartHover(bool isDaily, double canvasX, double canvasY)
    {
        var hover = isDaily ? DailyChartHover : MonthlyChartHover;
        if (canvasX < PlotLeft || canvasX > CanvasWidth - PlotRight ||
            canvasY < PlotTop || canvasY > CanvasHeight - PlotBottom)
        {
            hover.Hide();
            return;
        }

        var source = isDaily ? DailyKLines.ToList() : MonthlyKLines.ToList();
        ref var start = ref (isDaily ? ref _dailyChartStartIndex : ref _monthlyChartStartIndex);
        ref var visibleCount = ref (isDaily ? ref _dailyChartVisibleCount : ref _monthlyChartVisibleCount);
        var visible = GetVisibleKLines(source, ref start, ref visibleCount);
        if (visible.Count == 0)
        {
            hover.Hide();
            return;
        }

        var plotW = CanvasWidth - PlotLeft - PlotRight;
        var plotH = CanvasHeight - PlotTop - PlotBottom;
        var slotW = plotW / visible.Count;
        var index = Math.Clamp((int)Math.Floor((canvasX - PlotLeft) / slotW), 0, visible.Count - 1);
        var item = visible[index];
        var min = visible.Min(k => k.Low);
        var max = visible.Max(k => k.High);
        var span = Math.Max(0.0001m, max - min);
        var centerX = PlotLeft + (index + 0.5) * slotW;
        var closeY = PlotTop + (double)((max - item.Close) / span) * plotH;
        var changePct = item.Open == 0 ? 0 : (item.Close - item.Open) / item.Open * 100;
        var panelWidth = 300.0;
        var panelHeight = 170.0;
        var panelX = centerX + 18 + panelWidth <= CanvasWidth - PlotRight
            ? centerX + 18
            : centerX - panelWidth - 18;
        var panelY = Math.Clamp(closeY - panelHeight / 2, PlotTop + 6, CanvasHeight - PlotBottom - panelHeight - 6);
        var label = $"{item.Date:yyyy-MM-dd}\n" +
                    $"开盘 {item.Open:F2}    最高 {item.High:F2}\n" +
                    $"最低 {item.Low:F2}    收盘 {item.Close:F2}\n" +
                    $"涨跌 {changePct:+0.00;-0.00;0.00}%\n" +
                    $"成交量 {item.Volume:N0}";

        hover.Show(centerX, closeY, panelX, panelY, label, GetRiseFallBrush(item.Open, item.Close));
    }

    private static List<StockKLine> GetVisibleKLines(List<StockKLine> source, ref int start, ref int visibleCount)
    {
        var ordered = source.OrderBy(x => x.Date).ToList();
        EnsureChartWindow(ordered.Count, ref start, ref visibleCount);
        if (ordered.Count == 0)
            return ordered;

        var count = visibleCount == 0 ? ordered.Count : visibleCount;
        return ordered.Skip(start).Take(count).ToList();
    }

    private static void EnsureChartWindow(int sourceCount, ref int start, ref int visibleCount)
    {
        if (sourceCount <= 0)
        {
            start = 0;
            visibleCount = 0;
            return;
        }

        if (visibleCount <= 0 || visibleCount >= sourceCount)
        {
            start = 0;
            visibleCount = 0;
            return;
        }

        visibleCount = Math.Clamp(visibleCount, MinVisibleCandles(sourceCount), sourceCount);
        start = Math.Clamp(start, 0, Math.Max(0, sourceCount - visibleCount));
    }

    private static int MinVisibleCandles(int sourceCount) => Math.Min(sourceCount, Math.Max(8, sourceCount / 20));

    private static string BuildChartWindowLabel(List<StockKLine> visible, int totalCount)
    {
        if (visible.Count == 0)
            return "暂无数据";

        var first = visible.First();
        var last = visible.Last();
        var scope = visible.Count >= totalCount ? "显示全部" : $"显示 {visible.Count}/{totalCount} 根";
        return $"{scope} | {first.Date:yyyy-MM-dd} 至 {last.Date:yyyy-MM-dd}";
    }

    private static int NormalizeDailyDays(int value) => Math.Clamp(value, 5, 1200);
    private static int NormalizeMonthlyMonths(int value) => Math.Clamp(value, 1, 180);
    private static Brush GetRiseFallBrush(decimal open, decimal close) => close >= open ? Brushes.Red : Brushes.Green;

    private static void BuildChart(
        List<StockKLine> klines,
        ObservableCollection<CandleItem> candles,
        ObservableCollection<AxisTick> yTicks,
        ObservableCollection<AxisTick> xTicks)
    {
        candles.Clear();
        yTicks.Clear();
        xTicks.Clear();
        if (klines.Count == 0) return;

        var ordered = klines.OrderBy(x => x.Date).ToList();
        var min = ordered.Min(k => k.Low);
        var max = ordered.Max(k => k.High);
        var span = Math.Max(0.0001m, max - min);
        var plotW = CanvasWidth - PlotLeft - PlotRight;
        var plotH = CanvasHeight - PlotTop - PlotBottom;
        var slotW = plotW / Math.Max(1, ordered.Count);
        var bodyW = Math.Clamp(slotW * 0.56, 3, 18);

        double X(int i) => PlotLeft + (i + 0.5) * slotW;
        double Y(decimal p) => PlotTop + (double)((max - p) / span) * plotH;

        for (int i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var centerX = X(i);
            var openY = Y(current.Open);
            var closeY = Y(current.Close);
            var highY = Y(current.High);
            var lowY = Y(current.Low);
            var bodyTop = Math.Min(openY, closeY);
            var bodyHeight = Math.Max(2, Math.Abs(openY - closeY));
            var changePct = current.Open == 0 ? 0 : (current.Close - current.Open) / current.Open * 100;

            candles.Add(new CandleItem
            {
                X = centerX - bodyW / 2,
                BodyWidth = bodyW,
                BodyY = bodyTop,
                BodyHeight = bodyHeight,
                WickOffset = bodyW / 2,
                WickTop = highY,
                WickBottom = lowY,
                Fill = GetRiseFallBrush(current.Open, current.Close),
                Stroke = GetRiseFallBrush(current.Open, current.Close),
                ToolTip = $"时间: {current.Date:yyyy-MM-dd}\n开盘: {current.Open:F2}\n最高: {current.High:F2}\n最低: {current.Low:F2}\n收盘: {current.Close:F2}\n涨跌幅: {changePct:+0.00;-0.00;0.00}%\n成交量: {current.Volume:F0}"
            });
        }

        for (int t = 0; t <= 4; t++)
        {
            var v = max - t * span / 4;
            yTicks.Add(new AxisTick { X = 8, Y = PlotTop + t * plotH / 4 - 10, Label = v.ToString("F2") });
        }

        var tickIndexes = new[] { 0, ordered.Count / 3, (ordered.Count * 2) / 3, ordered.Count - 1 }.Distinct().OrderBy(x => x);
        foreach (var idx in tickIndexes)
            xTicks.Add(new AxisTick { X = X(idx) - 34, Y = CanvasHeight - 28, Label = ordered[idx].Date.ToString("yyyy-MM-dd") });
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

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void RaiseCommandStates()
    {
        (RunCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SendChatCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ReanalyzeCurrentChartRangeCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _role = "";
    private string _content = "";
    private DateTime _timestamp;

    public SessionChatMessage? Source { get; set; }

    public string Role
    {
        get => _role;
        set
        {
            if (_role == value)
                return;

            _role = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUser));
            OnPropertyChanged(nameof(RoleLabel));
        }
    }
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
                return;

            _content = value;
            OnPropertyChanged();
        }
    }
    public DateTime Timestamp
    {
        get => _timestamp;
        set
        {
            if (_timestamp == value)
                return;

            _timestamp = value;
            OnPropertyChanged();
        }
    }
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

public class AxisTick
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = "";
}

public class ChartHoverInfo : INotifyPropertyChanged
{
    private bool _isVisible;
    private double _x;
    private double _y;
    private double _markerX;
    private double _markerY;
    private double _panelX;
    private double _panelY;
    private string _label = "";
    private Brush _accent = Brushes.SlateGray;

    public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }
    public double X { get => _x; private set => SetProperty(ref _x, value); }
    public double Y { get => _y; private set => SetProperty(ref _y, value); }
    public double MarkerX { get => _markerX; private set => SetProperty(ref _markerX, value); }
    public double MarkerY { get => _markerY; private set => SetProperty(ref _markerY, value); }
    public double PanelX { get => _panelX; private set => SetProperty(ref _panelX, value); }
    public double PanelY { get => _panelY; private set => SetProperty(ref _panelY, value); }
    public string Label { get => _label; private set => SetProperty(ref _label, value); }
    public Brush Accent { get => _accent; private set => SetProperty(ref _accent, value); }

    public void Show(double x, double y, double panelX, double panelY, string label, Brush accent)
    {
        X = x;
        Y = y;
        MarkerX = x - 4;
        MarkerY = y - 4;
        PanelX = panelX;
        PanelY = panelY;
        Label = label;
        Accent = accent;
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public class CandleItem
{
    public double X { get; set; }
    public double BodyWidth { get; set; }
    public double BodyY { get; set; }
    public double BodyHeight { get; set; }
    public double WickOffset { get; set; }
    public double WickTop { get; set; }
    public double WickBottom { get; set; }
    public Brush Fill { get; set; } = Brushes.SteelBlue;
    public Brush Stroke { get; set; } = Brushes.SteelBlue;
    public string ToolTip { get; set; } = "";
}
