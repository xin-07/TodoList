using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TodoList.Database;
using TodoList.Repositories;
using TodoList.ViewModels;

namespace TodoList;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 装配：SQLite(单一权威源) → Repository → ViewModel。
            var db = new DatabaseService(Path.Combine(AppContext.BaseDirectory, "data", "todo.db"));
            var repo = new TodoRepository(db);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(repo),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}