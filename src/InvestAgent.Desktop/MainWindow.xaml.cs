using InvestAgent.Desktop.ViewModels;
using InvestAgent.Core.Models;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace InvestAgent.Desktop;

public partial class MainWindow : Window
{
    private bool _isDraggingChart;
    private bool _draggingDailyChart;
    private Point _lastChartDragPoint;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        AttachChatAutoScroll(vm);
    }

    private void AttachChatAutoScroll(MainViewModel vm)
    {
        vm.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
        foreach (var message in vm.ChatMessages)
            message.PropertyChanged += ChatMessage_PropertyChanged;
    }

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ChatMessageViewModel message in e.OldItems)
                message.PropertyChanged -= ChatMessage_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ChatMessageViewModel message in e.NewItems)
                message.PropertyChanged += ChatMessage_PropertyChanged;
        }

        QueueChatScrollToEnd();
    }

    private void ChatMessage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessageViewModel.Content))
            QueueChatScrollToEnd();
    }

    private void QueueChatScrollToEnd()
    {
        if (!ShouldAutoScrollChat())
            return;

        Dispatcher.BeginInvoke(new Action(ScrollChatToEnd), DispatcherPriority.Background);
    }

    private void ScrollChatToEnd()
    {
        ChatScrollViewer.ScrollToEnd();
    }

    private bool ShouldAutoScrollChat()
    {
        if (ChatScrollViewer.ScrollableHeight <= 0)
            return true;

        return ChatScrollViewer.VerticalOffset >= ChatScrollViewer.ScrollableHeight - 80;
    }

    private void ChatScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChatScrollViewer.ScrollToVerticalOffset(ChatScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void FlowDocumentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject root)
            return;

        var innerScrollViewer = FindVisualChildren<ScrollViewer>(root)
            .FirstOrDefault(x => x.ScrollableHeight > 0);
        if (innerScrollViewer is null)
            return;

        var nextOffset = Math.Clamp(
            innerScrollViewer.VerticalOffset - e.Delta * 0.9,
            0,
            innerScrollViewer.ScrollableHeight);
        innerScrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private void ChatInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            return;

        if (DataContext is not MainViewModel vm || !vm.SendChatCommand.CanExecute(null))
            return;

        e.Handled = true;
        vm.SendChatCommand.Execute(null);
    }

    private void AutoHideScrollViewer_MouseEnter(object sender, MouseEventArgs e)
    {
        SetScrollBarsRevealed(sender, true);
    }

    private void AutoHideScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(() => SetScrollBarsRevealed(sender, false)),
            DispatcherPriority.Loaded);
    }

    private void AutoHideScrollViewer_MouseLeave(object sender, MouseEventArgs e)
    {
        SetScrollBarsRevealed(sender, false);
    }

    private static void SetScrollBarsRevealed(object sender, bool revealed)
    {
        if (sender is not DependencyObject root)
            return;

        foreach (var scrollBar in FindVisualChildren<ScrollBar>(root))
        {
            if (scrollBar.Orientation != Orientation.Vertical)
                continue;

            scrollBar.Opacity = revealed ? 1 : 0;
            scrollBar.IsHitTestVisible = revealed;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void FlowDocumentScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        FitFlowDocumentToViewer(sender);
        AutoHideScrollViewer_Loaded(sender, e);
    }

    private void FlowDocumentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        FitFlowDocumentToViewer(sender);
    }

    private void FlowDocumentScrollViewer_LayoutUpdated(object? sender, EventArgs e)
    {
        FitFlowDocumentToViewer(sender);
    }

    private static void FitFlowDocumentToViewer(object? sender)
    {
        if (sender is not FlowDocumentScrollViewer viewer || viewer.Document is null)
            return;

        var width = Math.Max(260, viewer.ActualWidth - 18);
        if (Math.Abs(viewer.Document.PageWidth - width) < 0.5 &&
            Math.Abs(viewer.Document.ColumnWidth - width) < 0.5)
            return;

        viewer.Document.MinPageWidth = width;
        viewer.Document.PageWidth = width;
        viewer.Document.MaxPageWidth = width;
        viewer.Document.ColumnWidth = width;
        viewer.Document.ColumnGap = 0;
    }

    private void MonthlyChartCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        HandleChartMouseWheel(isDaily: false, sender, e);
    }

    private void DailyChartCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        HandleChartMouseWheel(isDaily: true, sender, e);
    }

    private void HandleChartMouseWheel(bool isDaily, object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Canvas canvas)
            return;

        var position = e.GetPosition(canvas);
        if (isDaily)
        {
            vm.ZoomDailyChartAt(position.X, e.Delta);
            vm.ShowDailyChartHover(position.X, position.Y);
        }
        else
        {
            vm.ZoomMonthlyChartAt(position.X, e.Delta);
            vm.ShowMonthlyChartHover(position.X, position.Y);
        }

        e.Handled = true;
    }

    private void MonthlyChartCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginChartDrag(isDaily: false, sender, e);
    }

    private void DailyChartCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginChartDrag(isDaily: true, sender, e);
    }

    private void BeginChartDrag(bool isDaily, object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Canvas canvas)
            return;

        if (e.ClickCount >= 2)
        {
            if (isDaily)
                vm.ResetDailyChart();
            else
                vm.ResetMonthlyChart();

            e.Handled = true;
            return;
        }

        _isDraggingChart = true;
        _draggingDailyChart = isDaily;
        _lastChartDragPoint = e.GetPosition(canvas);
        if (isDaily)
            vm.HideDailyChartHover();
        else
            vm.HideMonthlyChartHover();
        canvas.CaptureMouse();
        canvas.Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private void MonthlyChartCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        HandleChartMouseMove(isDaily: false, sender, e);
    }

    private void DailyChartCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        HandleChartMouseMove(isDaily: true, sender, e);
    }

    private void HandleChartMouseMove(bool isDaily, object sender, MouseEventArgs e)
    {
        if (sender is not Canvas canvas || DataContext is not MainViewModel vm)
            return;

        if (!_isDraggingChart || _draggingDailyChart != isDaily)
        {
            var hoverPosition = e.GetPosition(canvas);
            if (isDaily)
                vm.ShowDailyChartHover(hoverPosition.X, hoverPosition.Y);
            else
                vm.ShowMonthlyChartHover(hoverPosition.X, hoverPosition.Y);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndChartDrag(sender);
            return;
        }

        var position = e.GetPosition(canvas);
        var deltaX = position.X - _lastChartDragPoint.X;
        if (Math.Abs(deltaX) < 1)
            return;

        var moved = isDaily
            ? vm.PanDailyChartByPixels(deltaX)
            : vm.PanMonthlyChartByPixels(deltaX);

        if (moved || Math.Abs(deltaX) > 48)
            _lastChartDragPoint = position;

        e.Handled = true;
    }

    private void MonthlyChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingChart && DataContext is MainViewModel vm)
            vm.HideMonthlyChartHover();
    }

    private void DailyChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingChart && DataContext is MainViewModel vm)
            vm.HideDailyChartHover();
    }

    private void ChartCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndChartDrag(sender);
        e.Handled = true;
    }

    private void ChartCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        EndChartDrag(sender);
    }

    private void EndChartDrag(object? sender)
    {
        _isDraggingChart = false;
        if (sender is Canvas canvas && canvas.IsMouseCaptured)
            canvas.ReleaseMouseCapture();
    }

    private void NewsLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is not null && !string.IsNullOrWhiteSpace(e.Uri.ToString()))
            {
                Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                e.Handled = true;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void HistoryMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        BuildHistoryContextMenu(vm);
        HistoryContextMenu.PlacementTarget = HistoryMenuButton;
        HistoryContextMenu.IsOpen = true;
    }

    private void BuildHistoryContextMenu(MainViewModel vm)
    {
        HistoryContextMenu.Items.Clear();

        if (vm.HistoryGroups.Count == 0)
        {
            HistoryContextMenu.Items.Add(new MenuItem
            {
                Header = "暂无历史记录",
                IsEnabled = false
            });
            return;
        }

        foreach (var group in vm.HistoryGroups.OrderByDescending(x => x.Sessions.FirstOrDefault()?.UpdatedAt ?? DateTime.MinValue))
        {
            var stockItem = new MenuItem
            {
                Header = $"{group.Symbol} {group.StockName}",
                ToolTip = $"会话数：{group.Sessions.Count}"
            };

            foreach (var session in group.Sessions.OrderByDescending(x => x.UpdatedAt))
            {
                stockItem.Items.Add(BuildSessionMenuItem(vm, session));
            }

            HistoryContextMenu.Items.Add(stockItem);
        }
    }

    private MenuItem BuildSessionMenuItem(MainViewModel vm, AnalysisSessionRecord session)
    {
        var sessionItem = new MenuItem
        {
            Header = session.SessionTitle,
            ToolTip = $"更新时间：{session.UpdatedAt:yyyy-MM-dd HH:mm:ss}"
        };

        sessionItem.Click += async (_, _) => await vm.LoadHistorySessionAsync(session);
        sessionItem.PreviewMouseLeftButtonDown += async (_, args) =>
        {
            if (args.ClickCount >= 2)
            {
                await vm.LoadHistorySessionAsync(session);
                HistoryContextMenu.IsOpen = false;
                args.Handled = true;
            }
        };

        var contextMenu = new ContextMenu();

        var loadItem = new MenuItem { Header = "加载" };
        loadItem.Click += async (_, _) =>
        {
            await vm.LoadHistorySessionAsync(session);
            contextMenu.IsOpen = false;
            HistoryContextMenu.IsOpen = false;
        };

        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += async (_, _) =>
        {
            var result = MessageBox.Show(
                $"确认删除历史会话“{session.SessionTitle}”吗？",
                "删除历史记录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await vm.DeleteHistorySessionAsync(session);
            contextMenu.IsOpen = false;
            HistoryContextMenu.IsOpen = false;
        };

        contextMenu.Items.Add(loadItem);
        contextMenu.Items.Add(deleteItem);
        sessionItem.ContextMenu = contextMenu;
        return sessionItem;
    }
}
