using System.Windows.Input;
using System.Windows;

namespace InvestAgent.Desktop.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _executing;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _executing = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"执行过程中出现错误：{ex.Message}",
                "InvestAgent 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _executing = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
