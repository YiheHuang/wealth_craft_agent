using System.Windows.Input;
using System.Windows;

namespace InvestAgent.Desktop.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _executing;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (_canExecute?.Invoke(parameter) ?? true);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

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
