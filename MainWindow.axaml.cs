using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using TodoList.ViewModels;

namespace TodoList;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _notifications;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_notifications is null)
            _notifications = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.BottomRight,
            };

        if (DataContext is MainWindowViewModel vm)
            vm.DueAlarm += OnDueAlarm;
    }

    private void OnDueAlarm(object? sender, DueAlarmEventArgs e)
    {
        _notifications?.Show(new Notification("任务到期", e.Title, NotificationType.Warning));
    }

    private void OnNewTaskKeyDown(object? sender, KeyEventArgs e)
    {
        // 回车等同于点击"添加"。
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
        {
            if (vm.AddTaskCommand.CanExecute(null))
                vm.AddTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is TodoItemViewModel vm)
        {
            if (vm.BeginEditCommand.CanExecute(null))
                vm.BeginEditCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2)
            return;
        if (sender is Control c && c.DataContext is TodoItemViewModel vm)
        {
            if (vm.BeginEditCommand.CanExecute(null))
                vm.BeginEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not TodoItemViewModel vm)
            return;

        if (e.Key == Key.Enter)
        {
            if (vm.SaveEditCommand.CanExecute(null))
                vm.SaveEditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.CancelEditCommand.CanExecute(null))
                vm.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is TodoItemViewModel vm)
        {
            if (vm.SaveEditCommand.CanExecute(null))
                vm.SaveEditCommand.Execute(null);
        }
    }

    private void OnDueDateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (sender is Control c && c.DataContext is TodoItemViewModel vm)
        {
            if (vm.SetDueDateCommand.CanExecute(null))
                vm.SetDueDateCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDueDateLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is TodoItemViewModel vm)
        {
            if (vm.SetDueDateCommand.CanExecute(null))
                vm.SetDueDateCommand.Execute(null);
        }
    }
}