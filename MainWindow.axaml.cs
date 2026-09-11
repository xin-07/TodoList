using Avalonia.Controls;
using Avalonia.Input;
using TodoList.ViewModels;

namespace TodoList;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}