using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using TodoList.ViewModels;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// B3 结对重构的回归测试：
///  - T6：TodoItem.Title/FolderId 改为 SetProperty 后必须发出 PropertyChanged（否则视图依赖增量 diff 会"改完不刷新"）。
///  - T7：ApplyFilter 增量 diff 在切换视图/归属时对 DisplayItems 只做增删移动，不再 Clear 整体重建。
/// </summary>
public class B3NotificationAndFilterTests : IDisposable
{
    private readonly string _dbPath;

    public B3NotificationAndFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "todolist_b3_" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { }
        }
    }

    private TodoRepository NewRepository() => new(new DatabaseService(_dbPath));

    [Fact]
    public void TodoItem_Title与FolderId_修改时发出PropertyChanged()
    {
        var item = new TodoItem();
        var changed = new System.Collections.Generic.List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        item.Title = "新标题";
        item.FolderId = "folder-1";

        Assert.Contains(nameof(TodoItem.Title), changed);
        Assert.Contains(nameof(TodoItem.FolderId), changed);
    }

    [Fact]
    public void ApplyFilter_切换归属时_对DisplayItems只做增量改动不整体重建()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;

        repo.Add("任务A");
        repo.Add("任务B");
        repo.Add("任务C");

        var vm = new MainWindowViewModel(repo);

        // 初始在"全部"视图，DisplayItems 应含全部 3 条。
        Assert.Equal(3, vm.DisplayItems.Count);

        // 监听 DisplayItems：记录是否出现 Reset（整体重建）事件。
        var resets = 0;
        ((INotifyCollectionChanged)vm.DisplayItems).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        // 通过仓库把任务A、B 移入文件夹 → 当前"全部"视图仍应显示全部，且不作整体重建。
        repo.SetFolder(repo.Items.First(t => t.Title == "任务A").Id, folderId);
        repo.SetFolder(repo.Items.First(t => t.Title == "任务B").Id, folderId);

        Assert.Equal(3, vm.DisplayItems.Count);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void AddFolder_重建归属选项后_各行发出SelectedFolder变更通知()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        repo.Add("任务B");
        var taskId = repo.Items.First().Id;
        repo.AddFolder("工作");
        repo.SetFolder(taskId, repo.Folders.Single().Id);

        var vm = new MainWindowViewModel(repo);
        var row = vm.DisplayItems.First(v => v.Id == taskId);

        var notified = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TodoItemViewModel.SelectedFolder))
                notified++;
        };

        // 新建文件夹触发 _folderChoices 重建；行 VM 必须广播 SelectedFolder，
        // 否则 ComboBox 在 Clear 后停留在空选中，表现为下拉空白。
        repo.AddFolder("第二个文件夹");

        Assert.True(notified > 0, "重建归属选项后未发出 SelectedFolder 变更通知");
        Assert.Equal("工作", row.SelectedFolder.Name);
    }
}