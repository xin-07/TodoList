using System;
using System.IO;
using TodoList.Database;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// 数据路径解析规则的回归测试：开发 / 发布·Windows·macOS / 发布·Linux 三种落点，
/// 以及 config.json 读取与缺省兜底。平台与环境变量全部注入，不触碰真实宿主环境。
/// </summary>
public class DataPathsTests : IDisposable
{
    private readonly string _tempRoot;
    private static readonly DataPaths.DataSettings DevSettings =
        new(DataPaths.DefaultDataDirectory, DataPaths.DefaultUserDataDirectoryName, DataPaths.DefaultDbFileName);

    public DataPathsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "todolist_datapaths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void 开发模式_数据落项目根的数据目录()
    {
        var path = DataPaths.ResolveDatabasePath(
            _tempRoot, isDevelopment: true, HostPlatform.Windows, null, null, DevSettings);

        Assert.Equal(Path.Combine(_tempRoot, "data", "todo.db"), path);
    }

    [Fact]
    public void Windows发布_数据落exe同目录()
    {
        var exeDir = Path.Combine(_tempRoot, "win-x64");
        var path = DataPaths.ResolveDatabasePath(
            exeDir, isDevelopment: false, HostPlatform.Windows, "/xdg/ignored", "/home/user", DevSettings);

        Assert.Equal(Path.Combine(exeDir, "data", "todo.db"), path);
    }

    [Fact]
    public void macOS发布_数据落exe同目录()
    {
        var exeDir = Path.Combine(_tempRoot, "osx-arm64");
        var path = DataPaths.ResolveDatabasePath(
            exeDir, isDevelopment: false, HostPlatform.MacOs, null, "/Users/user", DevSettings);

        Assert.Equal(Path.Combine(exeDir, "data", "todo.db"), path);
    }

    [Fact]
    public void Linux发布_XDG绝对路径_数据落用户数据根的应用目录()
    {
        var xdg = Path.Combine(_tempRoot, "xdg");
        var path = DataPaths.ResolveDatabasePath(
            "/opt/todolist", isDevelopment: false, HostPlatform.Linux, xdg, "/home/user", DevSettings);

        Assert.Equal(Path.Combine(xdg, "todolist", "todo.db"), path);
    }

    [Fact]
    public void Linux发布_XDG为空_回退到home的local_share()
    {
        var home = Path.Combine(_tempRoot, "home");
        var path = DataPaths.ResolveDatabasePath(
            "/opt/todolist", isDevelopment: false, HostPlatform.Linux, null, home, DevSettings);

        Assert.Equal(Path.Combine(home, ".local", "share", "todolist", "todo.db"), path);
    }

    [Fact]
    public void Linux发布_XDG为相对路径_视为无效并回退home()
    {
        var home = Path.Combine(_tempRoot, "home");
        var path = DataPaths.ResolveDatabasePath(
            "/opt/todolist", isDevelopment: false, HostPlatform.Linux, "relative/xdg", home, DevSettings);

        Assert.Equal(Path.Combine(home, ".local", "share", "todolist", "todo.db"), path);
    }

    [Fact]
    public void Linux发布_XDG与home均不可用_显式报错()
    {
        Assert.Throws<InvalidOperationException>(() => DataPaths.ResolveDatabasePath(
            "/opt/todolist", isDevelopment: false, HostPlatform.Linux, "", null, DevSettings));
    }

    [Fact]
    public void Linux发布_使用config定义的应用目录名与库文件名()
    {
        var settings = new DataPaths.DataSettings("appdata", "custom-app", "tasks.db");
        var xdg = Path.Combine(_tempRoot, "xdg");
        var path = DataPaths.ResolveDatabasePath(
            "/opt/todolist", isDevelopment: false, HostPlatform.Linux, xdg, "/home/user", settings);

        Assert.Equal(Path.Combine(xdg, "custom-app", "tasks.db"), path);
    }

    [Fact]
    public void ReadDataSettings_config完整_读取权威值()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "config.json"), """
            {
              "data": {
                "directory": "appdata",
                "dbFileName": "tasks.db",
                "userDataDirectoryName": "custom-app"
              }
            }
            """);

        var settings = DataPaths.ReadDataSettings(_tempRoot);

        Assert.Equal("appdata", settings.Directory);
        Assert.Equal("tasks.db", settings.DbFileName);
        Assert.Equal("custom-app", settings.UserDataDirectoryName);
    }

    [Fact]
    public void ReadDataSettings_config缺失_回退缺省值()
    {
        var settings = DataPaths.ReadDataSettings(_tempRoot);

        Assert.Equal(DataPaths.DefaultDataDirectory, settings.Directory);
        Assert.Equal(DataPaths.DefaultDbFileName, settings.DbFileName);
        Assert.Equal(DataPaths.DefaultUserDataDirectoryName, settings.UserDataDirectoryName);
    }

    [Fact]
    public void ReadDataSettings_config缺字段_仅该字段回退缺省值()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "config.json"), """
            { "data": { "dbFileName": "tasks.db" } }
            """);

        var settings = DataPaths.ReadDataSettings(_tempRoot);

        Assert.Equal(DataPaths.DefaultDataDirectory, settings.Directory);
        Assert.Equal(DataPaths.DefaultUserDataDirectoryName, settings.UserDataDirectoryName);
        Assert.Equal("tasks.db", settings.DbFileName);
    }
}