using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using TodoList.Database;
using TodoList.Models;
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
            var projectRoot = TryFindProjectRoot();
            var programRoot = projectRoot ?? ResolveExecutableDirectory();
            var db = new DatabaseService(ResolveDatabasePath(programRoot, isDevelopment: projectRoot is not null));
            var repo = new TodoRepository(db);

            // 主题（阶段六）：启动读 config.json（宽容回退跟随系统）；初始值同时注入 VM（下拉匹配显示），
            // 运行时切换经注入回调完成"切变体 + 写盘"。
            var initialTheme = DataPaths.ReadTheme(programRoot);
            Application.Current!.RequestedThemeVariant = MapThemeVariant(initialTheme);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    repo,
                    theme =>
                    {
                        Application.Current!.RequestedThemeVariant = MapThemeVariant(theme);
                        return DataPaths.WriteTheme(programRoot, theme);
                    },
                    initialTheme,
                    backup: db),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>把主题枚举映射为 Avalonia ThemeVariant（Default=跟随系统明暗设置）。</summary>
    private static ThemeVariant MapThemeVariant(UiTheme theme) => theme switch
    {
        UiTheme.Light => ThemeVariant.Light,
        UiTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// 解析数据库绝对路径。
    /// 配置读自**程序根**（项目根 ?? exe 目录）的 config.json（单一权威源），与数据根分开计算；
    /// 数据落点规则（开发 / 发布·Linux / 发布·Windows·macOS）由 <see cref="DataPaths"/> 权威定义。
    /// </summary>
    private static string ResolveDatabasePath(string programRoot, bool isDevelopment)
    {
        var settings = DataPaths.ReadDataSettings(programRoot);

        var dbPath = DataPaths.ResolveDatabasePath(
            programRoot,
            isDevelopment: isDevelopment,
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