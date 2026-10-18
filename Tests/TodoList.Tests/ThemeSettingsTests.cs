using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using TodoList.ViewModels;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// 阶段六（T6.1 设置持久化 + T6.2 主题切换）回归测试：
///  - DataPaths.ReadTheme 宽容回退与损坏 JSON 行为；
///  - DataPaths.WriteTheme JsonNode 保留 data 节的保守写回与 round-trip；
///  - MainWindowViewModel SelectedTheme 的注入回调接线与初始主题显示。
/// 全部使用临时独立目录（不触碰真实 config.json / data/todo.db）。
/// </summary>
public class ThemeSettingsTests : IDisposable
{
    private readonly string _tempRoot;

    public ThemeSettingsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "todolist_theme_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch (IOException)
        {
            // 临时目录清理失败不致测试失败；内容含随机名不互相干扰。
        }
    }

    private string ConfigPath => Path.Combine(_tempRoot, "config.json");

    private const string OriginalDataOnlyConfig =
        """
        {
          "data": {
            "directory": "data",
            "dbFileName": "todo.db",
            "userDataDirectoryName": "todolist"
          }
        }
        """;

    // ---- T6.1 读取（宽容回退） ----

    [Fact]
    public void ReadTheme_文件缺失或节键缺失_回退跟随系统()
    {
        Assert.Equal(UiTheme.System, DataPaths.ReadTheme(_tempRoot));

        File.WriteAllText(ConfigPath, "{ \"data\": { \"directory\": \"data\" } }");
        Assert.Equal(UiTheme.System, DataPaths.ReadTheme(_tempRoot));

        File.WriteAllText(ConfigPath, "{ \"appearance\": { } }");
        Assert.Equal(UiTheme.System, DataPaths.ReadTheme(_tempRoot));
    }

    [Fact]
    public void ReadTheme_合法值_忽略大小写解析()
    {
        File.WriteAllText(ConfigPath, "{ \"appearance\": { \"theme\": \"Dark\" } }");
        Assert.Equal(UiTheme.Dark, DataPaths.ReadTheme(_tempRoot));

        File.WriteAllText(ConfigPath, "{ \"appearance\": { \"theme\": \"light\" } }");
        Assert.Equal(UiTheme.Light, DataPaths.ReadTheme(_tempRoot));

        File.WriteAllText(ConfigPath, "{ \"appearance\": { \"theme\": \"SYSTEM\" } }");
        Assert.Equal(UiTheme.System, DataPaths.ReadTheme(_tempRoot));
    }

    [Fact]
    public void ReadTheme_非法值_回退跟随系统()
    {
        File.WriteAllText(ConfigPath, "{ \"appearance\": { \"theme\": \"Neon\" } }");
        Assert.Equal(UiTheme.System, DataPaths.ReadTheme(_tempRoot));
    }

    // ---- T6.1 写入（保守保留其他节） ----

    [Fact]
    public void WriteTheme_文件不存在_创建仅含appearance的配置()
    {
        Assert.True(DataPaths.WriteTheme(_tempRoot, UiTheme.Dark));

        Assert.Equal(UiTheme.Dark, DataPaths.ReadTheme(_tempRoot));
        // 权威交叉验证：data 设置回退到默认（未写入 data 节）。
        var settings = DataPaths.ReadDataSettings(_tempRoot);
        Assert.Equal(DataPaths.DefaultDataDirectory, settings.Directory);
    }

    [Fact]
    public void WriteTheme_roundtrip_保留data节()
    {
        File.WriteAllText(ConfigPath, OriginalDataOnlyConfig);

        Assert.True(DataPaths.WriteTheme(_tempRoot, UiTheme.Light));

        // appearance 落盘。
        Assert.Equal(UiTheme.Light, DataPaths.ReadTheme(_tempRoot));

        // data 节完整保留（ReadDataSettings 仍读到原值）。
        var settings = DataPaths.ReadDataSettings(_tempRoot);
        Assert.Equal("data", settings.Directory);
        Assert.Equal("todo.db", settings.DbFileName);
        Assert.Equal("todolist", settings.UserDataDirectoryName);

        // 覆盖写入 Dark 也保留。
        Assert.True(DataPaths.WriteTheme(_tempRoot, UiTheme.Dark));
        Assert.Equal(UiTheme.Dark, DataPaths.ReadTheme(_tempRoot));
        Assert.Equal("todo.db", DataPaths.ReadDataSettings(_tempRoot).DbFileName);
    }

    [Fact]
    public void WriteTheme_configjson损坏_返回false且原文件不动()
    {
        const string broken = "{ \"data\":";
        File.WriteAllText(ConfigPath, broken);

        Assert.False(DataPaths.WriteTheme(_tempRoot, UiTheme.Dark));
        Assert.Equal(broken, File.ReadAllText(ConfigPath));
    }

    // ---- T6.2 行 VM 接线 ----

    [Fact]
    public void SelectedTheme_切换_注入回调被调用()
    {
        var called = new List<UiTheme>();
        var repo = new TodoRepository(new DatabaseService(Path.Combine(_tempRoot, "todo.db")));
        var vm = new MainWindowViewModel(repo, t => { called.Add(t); return true; });

        vm.SelectedTheme = vm.ThemeOptions.First(o => o.Value == UiTheme.Dark);

        Assert.Single(called);
        Assert.Equal(UiTheme.Dark, called[0]);
        Assert.Equal(UiTheme.Dark, vm.SelectedTheme.Value);
    }

    [Fact]
    public void InitialTheme_注入后下拉显示匹配()
    {
        var repo = new TodoRepository(new DatabaseService(Path.Combine(_tempRoot, "todo.db")));
        var vm = new MainWindowViewModel(repo, applyTheme: null, initialTheme: UiTheme.Light);

        Assert.Equal("浅色", vm.SelectedTheme.Label);
    }

    [Fact]
    public void SelectedTheme_未装配回调_仅本地更新不落盘()
    {
        var repo = new TodoRepository(new DatabaseService(Path.Combine(_tempRoot, "todo.db")));
        var vm = new MainWindowViewModel(repo);

        vm.SelectedTheme = vm.ThemeOptions.First(o => o.Value == UiTheme.Dark);

        Assert.Equal(UiTheme.Dark, vm.SelectedTheme.Value);
        Assert.False(File.Exists(ConfigPath)); // 无装配 = 不写真实程序根 config.json
    }
}
