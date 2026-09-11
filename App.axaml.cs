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
            // 数据写入项目根目录的 data/（与源码放一起，方便管理/备份）；
            // 打包发布环境下无 .csproj 标记，则回退到输出目录。
            var db = new DatabaseService(Path.Combine(ResolveDataDirectory(), "todo.db"));
            var repo = new TodoRepository(db);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(repo),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 解析数据目录：从程序输出目录向上查找项目根（含 *.csproj），
    /// 命中则返回其下 data 目录；未命中（如发布/嵌入）返回输出目录。
    /// </summary>
    private static string ResolveDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0
                || Directory.GetFiles(dir.FullName, "*.slnx").Length > 0)
            {
                return Path.Combine(dir.FullName, "data");
            }
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "data");
    }
}