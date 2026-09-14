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
    /// 数据目录与文件名一律读取 config.json（单一权威源）；找不到时回退默认 data/todo.db。
    /// 开发模式（向上定位到含 *.csproj/*.slnx 的项目根）数据落在项目根；
    /// 发布模式（单文件/不含项目根）回退到 exe 所在目录，避免把数据写进临时解压目录导致丢失。
    /// </summary>
    private static string ResolveDatabasePath()
    {
        const string DefaultDir = "data";
        const string DefaultFile = "todo.db";
        var dataDir = DefaultDir;
        var dbFileName = DefaultFile;

        var projectRoot = TryFindProjectRoot();
        var dataRoot = projectRoot ?? ResolveExecutableDirectory();

        // config.json 含数据目录定义；发布版通常不带，顺带旁读若无则用默认。
        var configPath = Path.Combine(dataRoot, "config.json");
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

        var dbPath = Path.Combine(dataRoot, dataDir, dbFileName);

        // 旧数据迁移：目标库不存在、且旧回退位置存在时，复制旧库到稳定路径（保留原文件，可回滚）。
        MigrateLegacyDatabase(dbPath, dataDir, dbFileName);

        return dbPath;
    }

    /// <summary>
    /// 从程序输出目录向上查找项目根（含 *.csproj 或 *.slnx）；未命中（发布/嵌入）返回 null。
    /// </summary>
    private static string? TryFindProjectRoot()
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
        return null;
    }

    /// <summary>可执行文件所在目录：发布模式数据的稳定落点（单文件运行时 AppContext.BaseDirectory 指向临时解压目录）。</summary>
    private static string ResolveExecutableDirectory()
        => Path.GetDirectoryName(Environment.ProcessPath)
           ?? AppContext.BaseDirectory;

    /// <summary>
    /// 若目标库不存在而旧回退位置（旧版曾用 AppContext.BaseDirectory 落库）有库，复制迁移到目标路径。
    /// 仅复制不移动，保留原文件以便回滚；复制失败不阻断启动。
    /// </summary>
    private static void MigrateLegacyDatabase(string targetDb, string dataDir, string dbFileName)
    {
        if (File.Exists(targetDb))
            return;

        var legacy = Path.Combine(AppContext.BaseDirectory, dataDir, dbFileName);
        if (!File.Exists(legacy))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDb)!);
            File.Copy(legacy, targetDb);
        }
        catch (IOException)
        {
            // 迁移失败不应阻断启动；数据仍在旧位置。
        }
    }
}