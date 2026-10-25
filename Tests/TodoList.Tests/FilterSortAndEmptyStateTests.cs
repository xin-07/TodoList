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
/// 阶段四（视图与交互）回归测试：
///  - T4.2 状态/优先级筛选、三种排序（与标题搜索、边栏视图取交集；增量 diff 不触发集合重置）；
///  - T4.3 空状态三态文案与「清除筛选」（重置状态/优先级/搜索，保留排序方式与边栏视图）。
/// </summary>
public class FilterSortAndEmptyStateTests : IDisposable
{
    private readonly string _dbPath;

    public FilterSortAndEmptyStateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "todolist_stage4_" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException)
            {
                // 临时库删除失败不致测试失败；内容含随机名不互相干扰。
            }
        }
    }

    private TodoRepository NewRepository() => new(new DatabaseService(_dbPath));

    // ---- T4.2 筛选 ----

    [Fact]
    public void StatusFilter_未完成与已完成_各自只显示对应任务()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        repo.Add("任务B");
        repo.Add("任务C");
        var vm = new MainWindowViewModel(repo);

        repo.SetCompleted(repo.Items.First(t => t.Title == "任务B").Id, true);

        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusIncomplete);
        Assert.Equal(2, vm.DisplayItems.Count);
        Assert.All(vm.DisplayItems, v => Assert.False(v.Item.IsCompleted));

        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusCompleted);
        Assert.Equal("任务B", Assert.Single(vm.DisplayItems).Title);
    }

    [Fact]
    public void PriorityFilter_高_只显示高优先级任务()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        repo.Add("任务B");
        var vm = new MainWindowViewModel(repo);

        repo.SetPriority(repo.Items.First(t => t.Title == "任务A").Id, TaskPriority.High);

        vm.PriorityFilter = vm.PriorityFilterOptions.First(o => o.Value == TaskPriority.High);
        Assert.Equal("任务A", Assert.Single(vm.DisplayItems).Title);
    }

    [Fact]
    public void 状态优先级筛选与标题搜索及边栏视图_取交集()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;
        repo.Add("写周报");
        repo.Add("写月报");
        repo.Add("买菜");

        var vm = new MainWindowViewModel(repo);
        repo.SetFolder(repo.Items.First(t => t.Title == "写周报").Id, folderId);
        repo.SetFolder(repo.Items.First(t => t.Title == "写月报").Id, folderId);
        repo.SetPriority(repo.Items.First(t => t.Title == "写周报").Id, TaskPriority.High);

        // 视图：工作 → 2 条。
        vm.SelectedSidebar = vm.SidebarItems.First(i => i.Key == folderId);
        Assert.Equal(2, vm.DisplayItems.Count);

        // + 优先级=高 → 1 条。
        vm.PriorityFilter = vm.PriorityFilterOptions.First(o => o.Value == TaskPriority.High);
        Assert.Equal("写周报", Assert.Single(vm.DisplayItems).Title);

        // + 状态=未完成（写周报未完成，交集不变）+ 标题搜索"月" → 交集为空。
        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusIncomplete);
        vm.NewTaskTitle = "月";
        Assert.Empty(vm.DisplayItems);
    }

    // ---- T4.2 排序 ----

    [Fact]
    public void SortBy_截止日期_由近到远且无日期排最后()
    {
        var repo = NewRepository();
        repo.Add("甲");
        repo.Add("乙");
        repo.Add("丙");
        repo.SetDueDate(repo.Items.First(t => t.Title == "甲").Id, DateTime.Today.AddDays(5));
        repo.SetDueDate(repo.Items.First(t => t.Title == "乙").Id, DateTime.Today.AddDays(1));

        var vm = new MainWindowViewModel(repo);
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortDueDate);

        Assert.Equal(new[] { "乙", "甲", "丙" }, vm.DisplayItems.Select(v => v.Title));
    }

    [Fact]
    public void SortBy_创建时间_由新到旧()
    {
        var repo = NewRepository();
        repo.Add("旧任务");
        repo.Add("中任务");
        repo.Add("新任务");

        // 仓库无 CreatedAt 写入口（新增时以当前时间写定，连续新增时间戳分辨率不足），
        // 测试直接设定三个明确时间以获得确定顺序；仅在测试中做模型级排布。
        repo.Items.First(t => t.Title == "旧任务").CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0);
        repo.Items.First(t => t.Title == "中任务").CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0);
        repo.Items.First(t => t.Title == "新任务").CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0);

        var vm = new MainWindowViewModel(repo);
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortCreatedAt);

        Assert.Equal(new[] { "新任务", "中任务", "旧任务" }, vm.DisplayItems.Select(v => v.Title));
    }

    [Fact]
    public void SortBy_默认优先级_高到低保持原行为()
    {
        var repo = NewRepository();
        repo.Add("高任务");
        repo.Add("低任务");
        var vm = new MainWindowViewModel(repo);

        // 默认排序（优先级）下，落库低优先级后应重排到无优先级任务之后。
        repo.SetPriority(repo.Items.First(t => t.Title == "低任务").Id, TaskPriority.Low);

        Assert.Equal(new[] { "低任务", "高任务" }, vm.DisplayItems.Select(v => v.Title));
    }

    // ---- T4.2.4 已完成沉底（勾选后沉底；组内创建时间新→旧；全局恒定；取消归位） ----

    [Fact]
    public void 勾选完成后_任务沉底到已完成区_默认排序下同样生效()
    {
        var repo = NewRepository();
        repo.Add("甲");
        repo.Add("丙");
        var vm = new MainWindowViewModel(repo);

        // 乙定高优先级后置顶，默认序为 [乙, 甲, 丙]。
        repo.Add("乙");
        repo.SetPriority(repo.Items.First(t => t.Title == "乙").Id, TaskPriority.High);
        Assert.Equal(new[] { "乙", "甲", "丙" }, vm.DisplayItems.Select(v => v.Title));

        // 完成低优先级且无日期的甲，也要越过未完成区沉到最底。
        repo.SetCompleted(repo.Items.First(t => t.Title == "甲").Id, true);
        Assert.Equal(new[] { "乙", "丙", "甲" }, vm.DisplayItems.Select(v => v.Title));
    }

    [Fact]
    public void 已完成区内部_恒按创建时间新到旧排列()
    {
        var repo = NewRepository();
        repo.Add("晚建先勾");
        repo.Add("早建后勾");

        // 仓库无 CreatedAt 写入口（与既有"创建时间排序"用例同因），测试直接设定明确时间。
        repo.Items.First(t => t.Title == "晚建先勾").CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0);
        repo.Items.First(t => t.Title == "早建后勾").CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0);

        var vm = new MainWindowViewModel(repo);

        // "晚建先勾"先完成，"早建后勾"后完成；沉底段内仍按创建时间新→旧，而非勾选先后。
        repo.SetCompleted(repo.Items.First(t => t.Title == "晚建先勾").Id, true);
        repo.SetCompleted(repo.Items.First(t => t.Title == "早建后勾").Id, true);

        Assert.Equal(new[] { "晚建先勾", "早建后勾" }, vm.DisplayItems.Select(v => v.Title));
    }

    [Fact]
    public void 切换排序后_已完成区仍沉底且组内创建时间新到旧不变()
    {
        var repo = NewRepository();
        repo.Add("远期");
        repo.Add("无期");
        repo.Add("新完成");
        var vm = new MainWindowViewModel(repo);

        repo.SetCompleted(repo.Items.First(t => t.Title == "远期").Id, true);
        repo.SetCompleted(repo.Items.First(t => t.Title == "新完成").Id, true);

        // 给已完成的"远期"设最近截止日期：切到截止排序后若不沉底将排未完成区之前。
        repo.SetDueDate(repo.Items.First(t => t.Title == "远期").Id, DateTime.Today.AddDays(1));
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortDueDate);

        // 未完成区只剩"无期"；已完成区保持创建时间新→旧（新完成 > 远期）。
        Assert.Equal(new[] { "无期", "新完成", "远期" }, vm.DisplayItems.Select(v => v.Title));
    }

    [Fact]
    public void 取消勾选后_按当前排序规则归位到未完成区()
    {
        var repo = NewRepository();
        repo.Add("甲");
        repo.Add("乙");
        var vm = new MainWindowViewModel(repo);

        repo.SetCompleted(repo.Items.First(t => t.Title == "甲").Id, true);
        Assert.Equal(new[] { "乙", "甲" }, vm.DisplayItems.Select(v => v.Title));

        // 取消完成：同为普通优先级，未完成区并列时按创建时间旧→新，回到原位。
        repo.SetCompleted(repo.Items.First(t => t.Title == "甲").Id, false);
        Assert.Equal(new[] { "甲", "乙" }, vm.DisplayItems.Select(v => v.Title));
    }

    // ---- 增量 diff 回归 ----

    [Fact]
    public void 切换筛选与排序_对DisplayItems不触发集合Reset()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        repo.Add("任务B");
        var vm = new MainWindowViewModel(repo);

        var resets = 0;
        ((INotifyCollectionChanged)vm.DisplayItems).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusCompleted);
        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusAll);
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortDueDate);
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortPriority);
        vm.PriorityFilter = vm.PriorityFilterOptions.First(o => o.Value == TaskPriority.High);

        Assert.Equal(0, resets);
    }

    // ---- T4.3 空状态 ----

    [Fact]
    public void EmptyState_库内无任务_提示创建第一条且无激活筛选()
    {
        var vm = new MainWindowViewModel(NewRepository());

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasActiveFilter);
        Assert.Equal("暂无任务，输入标题后回车创建第一条", vm.EmptyStateText);
    }

    [Fact]
    public void EmptyState_有任务但搜索无匹配_提示无匹配()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var vm = new MainWindowViewModel(repo);

        vm.NewTaskTitle = "不存在";
        Assert.True(vm.IsEmpty);
        Assert.True(vm.HasActiveFilter);
        Assert.Equal("没有符合筛选条件的任务", vm.EmptyStateText);
    }

    [Fact]
    public void EmptyState_有空文件夹视图_提示此视图暂无任务()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;
        repo.Add("任务A");
        var vm = new MainWindowViewModel(repo);

        // 视图（工作文件夹）为空但无激活筛选：非"无匹配"，走"此视图暂无任务"分支。
        vm.SelectedSidebar = vm.SidebarItems.First(i => i.Key == folderId);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasActiveFilter);
        Assert.Equal("此视图暂无任务", vm.EmptyStateText);
    }

    // ---- T4.3 清除筛选 ----

    [Fact]
    public void ClearFilters_重置状态优先级与搜索_保留排序与边栏视图()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;
        repo.Add("高任务");
        var vm = new MainWindowViewModel(repo);

        vm.SelectedSidebar = vm.SidebarItems.First(i => i.Key == folderId);
        vm.StatusFilter = vm.StatusFilterOptions.First(o => o.Key == MainWindowViewModel.StatusCompleted);
        vm.PriorityFilter = vm.PriorityFilterOptions.First(o => o.Value == TaskPriority.High);
        vm.SortBy = vm.SortOptions.First(o => o.Key == MainWindowViewModel.SortDueDate);
        vm.NewTaskTitle = "x";
        Assert.True(vm.HasActiveFilter);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(MainWindowViewModel.StatusAll, vm.StatusFilter.Key);
        Assert.Null(vm.PriorityFilter.Value);
        Assert.Equal("", vm.NewTaskTitle);
        // 排序方式与边栏视图保留。
        Assert.Equal(MainWindowViewModel.SortDueDate, vm.SortBy.Key);
        Assert.Equal(folderId, vm.SelectedSidebar?.Key);
        Assert.False(vm.HasActiveFilter);
    }
}
