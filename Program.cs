using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TodoList;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 全局兜底：任何未被处理的异常都落盘到输出目录 logs/，便于离线诊断
        // （Windows 事件日志的堆栈常被截断）。仅在异常时写盘，不影响正常路径。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    /// <summary>把异常与堆栈追加写入日志文件（追加模式，单条记录可独立观测）。</summary>
    private static void LogCrash(string origin, Exception? ex)
    {
        if (ex is null)
            return;

        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:O}] [{origin}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败不应二次中断/掩盖原始崩溃；忽略即可。
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
