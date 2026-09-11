using System;
using System.IO;
using System.Text.Json;
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
            // 数据路径由 config.json 单一权威定义，禁止此处硬编码。
            var db = new DatabaseService(ResolveDatabasePath());
            var repo = new TodoRepository(db);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(repo),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 解析数据库绝对路径。
    /// 数据目录与文件名一律读取项目根 config.json（单一权威源）；
    /// 找不到 config.json 时（如发布/嵌入环境）回退默认 data/todo.db 于输出目录。
    /// </summary>
    private static string ResolveDatabasePath()
    {
        var root = ResolveProjectRoot();

        const string DefaultDir = "data";
        const string DefaultFile = "todo.db";
        var dataDir = DefaultDir;
        var dbFileName = DefaultFile;

        var configPath = Path.Combine(root, "config.json");
        if (File.Exists(configPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("directory", out var dir) && dir.ValueKind == JsonValueKind.String)
                    dataDir = dir.GetString() ?? DefaultDir;
                if (data.TryGetProperty("dbFileName", out var file) && file.ValueKind == JsonValueKind.String)
                    dbFileName = file.GetString() ?? DefaultFile;
            }
        }

        return Path.Combine(root, dataDir, dbFileName);
    }

    /// <summary>
    /// 从程序输出目录向上查找项目根（含 *.csproj 或 *.slnx）；
    /// 未命中（如发布/嵌入）回退到程序输出目录。
    /// </summary>
    private static string ResolveProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0
                || Directory.GetFiles(dir.FullName, "*.slnx").Length > 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}