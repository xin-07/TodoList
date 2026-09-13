using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        {
            vm.DueAlarm += OnDueAlarm;
            vm.DeleteFolderRequested += OnDeleteFolderRequested;
        }
    }

    private void OnDueAlarm(object? sender, DueAlarmEventArgs e)
    {
        _notifications?.Show(new Notification("任务到期", e.Title, NotificationType.Warning));
    }

    private void OnDeleteFolderRequested(object? sender, DeleteFolderConfirmEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // 简单确认对话框（Avalonia 无内置 MessageBox，用独立 Window 实现）。
        var item = e.Item;
        var dialog = new Window
        {
            Width = 380,
            Height = 180,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "删除文件夹",
        };

        var message = new TextBlock
        {
            Text = $"删除文件夹“{item.Name}”将连同其中的 {item.ItemCount} 条条目一并删除，此操作不可撤销。确定？",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
            Margin = new Thickness(20, 20, 20, 10),
        };

        var cancel = new Button { Content = "取消", Width = 80 };
        var ok = new Button { Content = "删除", Width = 80, Foreground = Brushes.Red };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 16, 16),
            Children = { cancel, ok },
        };
        var panel = new StackPanel
        {
            Children = { message, buttons },
        };
        dialog.Content = panel;

        ok.Click += (_, _) =>
        {
            vm.ConfirmDeleteFolder(item);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        // 作为模态子窗口等待（emptyTask 占位是为了让 handler 不阻塞调用方）。
        var _ = dialog.ShowDialog(this);
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

    private void OnNewFolderKeyDown(object? sender, KeyEventArgs e)
    {
        // 回车等同于点击"新建文件夹"。
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
        {
            if (vm.AddFolderCommand.CanExecute(null))
                vm.AddFolderCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 点击其它区域时退出编辑：
    ///  - 新建输入区：若点击不在新建输入区内，收起输入区（放弃未确认输入）。
    ///  - 文件夹重命名：若点击不在正在重命名的文件夹项内，触发保存退出。
    /// 点击发生在编辑区内部时不处理，避免误退出。
    /// </summary>
    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // 命中控件向上找到的 SidebarItemViewModel（若点击落在某文件夹项内则为它）。
        var hitItem = FindSidebarItem(e.Source as Avalonia.Visual);

        // 新建输入区：点击区外部 => 收起。
        if (vm.IsNewFolderOpen)
        {
            var insideNew = IsSourceWithin(e.Source as Avalonia.Visual, NewFolderInput);
            if (!insideNew)
                vm.IsNewFolderOpen = false;
        }

        // 文件夹重命名：若点击落在正在重命名的项内部则保留编辑；否则保存退出。
        foreach (var item in vm.SidebarItems)
        {
            if (!item.IsRenaming)
                continue;
            // 点击落在该重命名项内部（含其编辑框/确认按钮）=> 不退出。
            if (ReferenceEquals(hitItem, item))
                return;
            // 点击其它区域 => 保存退出该重命名。
            if (item.SaveRenameCommand.CanExecute(null))
                item.SaveRenameCommand.Execute(null);
            return; // 同一时刻仅一个重命名态
        }
    }

    /// <summary>向上查找点击源所在文件夹项（DataContext 为 SidebarItemViewModel）。</summary>
    private static SidebarItemViewModel? FindSidebarItem(Avalonia.Visual? source)
    {
        StyledElement? n = source as StyledElement;
        while (n is not null)
        {
            if (n is Control c && c.DataContext is SidebarItemViewModel item)
                return item;
            n = n.Parent;
        }
        return null;
    }

    /// <summary>判断点击源是否为 <paramref name="container"/> 的后代（或本身）。</summary>
    private static bool IsSourceWithin(Avalonia.Visual? source, Control? container)
    {
        if (container is null)
            return false;
        StyledElement? n = source as StyledElement;
        while (n is not null)
        {
            if (ReferenceEquals(n, container))
                return true;
            n = n.Parent;
        }
        return false;
    }

    private void OnFolderNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SidebarItemViewModel item)
        {
            if (item.BeginRenameCommand.CanExecute(null))
                item.BeginRenameCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnFolderRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not SidebarItemViewModel item)
            return;

        if (e.Key == Key.Enter)
        {
            if (item.SaveRenameCommand.CanExecute(null))
                item.SaveRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (item.CancelRenameCommand.CanExecute(null))
                item.CancelRenameCommand.Execute(null);
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