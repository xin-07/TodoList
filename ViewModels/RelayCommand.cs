using System;
using System.Windows.Input;

namespace TodoList.ViewModels;

/// <summary>
/// 极简 ICommand 实现，避免引入额外 MVVM 框架。
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    // CanExecute 在本 MVP 中恒为 true，命令可用性从不动态变化，
    // 因此该事件无需实际使用。保留以满足 ICommand 接口契约。
    #pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
    #pragma warning restore CS0067
}