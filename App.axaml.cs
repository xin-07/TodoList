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
    /// 配置读自**程序根**（项目根 ?? exe 目录）的 config.json（单一权威源），与数据根分开计算；
    /// 数据落点规则（开发 / 发布·Linux / 发布·Windows·macOS）由 <see cref="DataPaths"/> 权威定义。
    /// </summary>
    private static string ResolveDatabasePath()
    {
        var projectRoot = TryFindProjectRoot();
        var programRoot = projectRoot ?? ResolveExecutableDirectory();
        var settings = DataPaths.ReadDataSettings(programRoot);

        var dbPath = DataPaths.ResolveDatabasePath(
            programRoot,
            isDevelopment: projectRoot is not null,
            platform: DataPaths.DetectHostPlatform(),
            xdgDataHome: Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            homeDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            settings: settings);

        // 旧数据迁移：目标库不存在、且旧回退位置存在时，复制旧库到稳定路径（保留原文件，可回滚）。
        MigrateLegacyDatabase(dbPath, settings.Directory, settings.DbFileName);

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