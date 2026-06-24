using System.Windows.Input;
using System.Windows;

namespace InvestAgent.Desktop.ViewModels;

/// <summary>
/// 通用的异步 RelayCommand 实现。
/// 支持异步执行、执行中禁用（防重复点击）以及异常消息弹窗。
/// 用于 WPF 中 ViewModel 与 View 之间的命令绑定。
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>是否正在执行中（用于防抖）</summary>
    private bool _executing;

    /// <summary>
    /// 创建不带参数的命令（简化重载）。
    /// </summary>
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>
    /// 创建带参数的命令。
    /// </summary>
    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>判断命令是否可执行：未在执行中 且 自定义条件满足</summary>
    public bool CanExecute(object? parameter) => !_executing && (_canExecute?.Invoke(parameter) ?? true);

    /// <summary>通知 UI 刷新 CanExecute 状态</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 异步执行命令。
    /// 执行前将 _executing 置为 true（禁用按钮），完成后恢复。
    /// 异常通过 MessageBox 弹窗提示用户。
    /// </summary>
    public async void Execute(object? parameter)
    {
        _executing = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            // 异常弹窗——递归展开 InnerException 获取完整错误链
            MessageBox.Show(
                $"执行过程中出现错误：{BuildExceptionMessage(ex)}",
                "InvestAgent 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _executing = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>递归展开异常链中的所有消息，去重后拼接</summary>
    private static string BuildExceptionMessage(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
                messages.Add(current.Message);
        }

        return string.Join("\n", messages.Distinct());
    }
}
