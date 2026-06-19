using InvestAgent.Desktop.ViewModels;
using InvestAgent.Core.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace InvestAgent.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
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
