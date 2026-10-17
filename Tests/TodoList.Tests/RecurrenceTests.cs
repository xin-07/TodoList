using System;
using System.IO;
using System.Linq;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using TodoList.ViewModels;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// 阶段三（周期性任务·滚动式）回归测试：
///  - T3.2 规则引擎纯函数（四种周期 + 间隔 + 月末 clamp + 摘要文本）；
///  - T3.3 仓库（SetRecurrence 校验与双路径落库、SetCompleted 单步滚动/递减/取尽定格、清截止日期联清）；
///  - T3.4 行 VM（应用重复设置的解析校验与摘要回填）。
/// 全部使用临时独立 SQLite 库（不触碰 data/todo.db）。
/// </summary>
public class RecurrenceTests : IDisposable
{
    private readonly string _dbPath;

    public RecurrenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "todolist_recurrence_" + Guid.NewGuid().ToString("N") + ".db");
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

    // ---- T3.2 规则引擎 ----

    [Fact]
    public void NextDue_四种周期与间隔_按单位推进()
    {
        var due = new DateTime(2026, 10, 17);

        Assert.Equal(new DateTime(2026, 10, 18), Recurrence.NextDue(due, RecurrenceType.Daily, 1));
        Assert.Equal(new DateTime(2026, 10, 20), Recurrence.NextDue(due, RecurrenceType.Daily, 3));
        Assert.Equal(new DateTime(2026, 10, 24), Recurrence.NextDue(due, RecurrenceType.Weekly, 1));
        Assert.Equal(due.AddDays(14), Recurrence.NextDue(due, RecurrenceType.Weekly, 2));
        Assert.Equal(new DateTime(2026, 11, 17), Recurrence.NextDue(due, RecurrenceType.Monthly, 1));
        Assert.Equal(new DateTime(2027, 10, 17), Recurrence.NextDue(due, RecurrenceType.Yearly, 1));
    }

    [Fact]
    public void NextDue_月末溢出_自动clamp到月末()
    {
        // 1 月 31 日 +1 月 → 2 月 28 日（2026 非闰年）。
        Assert.Equal(new DateTime(2026, 2, 28), Recurrence.NextDue(new DateTime(2026, 1, 31), RecurrenceType.Monthly, 1));

        // 闰年 2 月 29 日 +1 年 → 2025-02-28。
        Assert.Equal(new DateTime(2025, 2, 28), Recurrence.NextDue(new DateTime(2024, 2, 29), RecurrenceType.Yearly, 1));
    }

    [Fact]
    public void ToDisplayText_摘要文本正确()
    {
        Assert.Null(Recurrence.ToDisplayText(RecurrenceType.None, 1, null));
        Assert.Equal("每周", Recurrence.ToDisplayText(RecurrenceType.Weekly, 1, null));
        Assert.Equal("每 3 天", Recurrence.ToDisplayText(RecurrenceType.Daily, 3, null));
        Assert.Equal("每 2 周 · 剩 5 次", Recurrence.ToDisplayText(RecurrenceType.Weekly, 2, 5));
        Assert.Equal("每月 · 剩 1 次", Recurrence.ToDisplayText(RecurrenceType.Monthly, 1, 1));
    }

    // ---- T3.3 仓库 ----

    [Fact]
    public void SetRecurrence_未设截止日期_被拒绝()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;

        Assert.False(repo.SetRecurrence(id, RecurrenceType.Weekly, 1, null));
        Assert.Equal(RecurrenceType.None, repo.Items.Single().Recurrence);
    }

    [Fact]
    public void SetRecurrence_非法间隔或次数_被拒绝()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        repo.SetDueDate(id, DateTime.Today.AddDays(7));

        Assert.False(repo.SetRecurrence(id, RecurrenceType.Daily, 0, null));
        Assert.False(repo.SetRecurrence(id, RecurrenceType.Daily, 1, 0));
        Assert.False(repo.SetRecurrence(id, RecurrenceType.Daily, -1, null));

        // 非法请求不落库、不污染投影。
        Assert.Equal(RecurrenceType.None, repo.Items.Single().Recurrence);
    }

    [Fact]
    public void SetRecurrence_合法规则_双路径一致()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        repo.SetDueDate(id, DateTime.Today.AddDays(7));

        Assert.True(repo.SetRecurrence(id, RecurrenceType.Weekly, 2, 5));

        var projected = repo.Items.Single();
        Assert.Equal(RecurrenceType.Weekly, projected.Recurrence);
        Assert.Equal(2, projected.RecurrenceInterval);
        Assert.Equal(5, projected.RecurrenceRemaining);

        // 直接查权威源交叉验证。
        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.Equal(RecurrenceType.Weekly, fromDb.Recurrence);
        Assert.Equal(2, fromDb.RecurrenceInterval);
        Assert.Equal(5, fromDb.RecurrenceRemaining);
    }

    [Fact(Skip = "[DEFERRED 2026-10-18] 阶段三滚动分支已注释停用（TodoRepository.SetCompleted）；重新启用后移除 Skip，且需先复现修复用户实测'完成后日期未推进'缺陷")]
    public void SetCompleted_无限周期_滚动到下一期且保持未完成()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        var due = DateTime.Today;
        repo.SetDueDate(id, due);
        repo.SetRecurrence(id, RecurrenceType.Daily, 1, null);

        repo.SetCompleted(id, true);

        var projected = repo.Items.Single();
        Assert.False(projected.IsCompleted);
        Assert.Equal(due.AddDays(1), projected.DueDate);
        Assert.Equal(RecurrenceType.Daily, projected.Recurrence);
        Assert.Null(projected.RecurrenceRemaining);

        // 权威源交叉验证：单条 SQL 已把完成态归零、截止日期推进。
        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.False(fromDb.IsCompleted);
        Assert.Equal(due.AddDays(1), fromDb.DueDate);
    }

    [Fact(Skip = "[DEFERRED 2026-10-18] 阶段三滚动分支已注释停用（TodoRepository.SetCompleted）；重新启用后移除 Skip")]
    public void SetCompleted_有限周期_剩余次数递减()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        repo.SetDueDate(id, DateTime.Today.AddDays(7));
        repo.SetRecurrence(id, RecurrenceType.Weekly, 1, 3);

        repo.SetCompleted(id, true);

        var projected = repo.Items.Single();
        Assert.False(projected.IsCompleted);
        Assert.Equal(2, projected.RecurrenceRemaining);
        Assert.Equal(DateTime.Today.AddDays(7).AddDays(7), projected.DueDate);

        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.Equal(2, fromDb.RecurrenceRemaining);
    }

    [Fact(Skip = "[DEFERRED 2026-10-18] 阶段三滚动分支已注释停用（TodoRepository.SetCompleted）；重新启用后移除 Skip")]
    public void SetCompleted_剩余一次_完成定格并清除周期()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        var due = DateTime.Today;
        repo.SetDueDate(id, due);
        repo.SetRecurrence(id, RecurrenceType.Monthly, 1, 1);

        repo.SetCompleted(id, true);

        var projected = repo.Items.Single();
        Assert.True(projected.IsCompleted);
        Assert.Equal(RecurrenceType.None, projected.Recurrence);
        Assert.Equal(1, projected.RecurrenceInterval);
        Assert.Null(projected.RecurrenceRemaining);
        // 截止日期不再推进（归为普通任务）。
        Assert.Equal(due, projected.DueDate);

        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.True(fromDb.IsCompleted);
        Assert.Equal(RecurrenceType.None, fromDb.Recurrence);
        Assert.Null(fromDb.RecurrenceRemaining);
    }

    [Fact]
    public void SetCompleted_普通任务_不滚动保持现状()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;

        repo.SetCompleted(id, true);

        var projected = repo.Items.Single();
        Assert.True(projected.IsCompleted);
        Assert.Equal(RecurrenceType.None, projected.Recurrence);
        Assert.Null(projected.DueDate);
    }

    [Fact]
    public void SetDueDate_清空截止日期_联清周期规则()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var id = repo.Items.Single().Id;
        repo.SetDueDate(id, DateTime.Today.AddDays(7));
        repo.SetRecurrence(id, RecurrenceType.Weekly, 2, 5);

        repo.SetDueDate(id, null);

        var projected = repo.Items.Single();
        Assert.Null(projected.DueDate);
        Assert.Equal(RecurrenceType.None, projected.Recurrence);
        Assert.Equal(1, projected.RecurrenceInterval);
        Assert.Null(projected.RecurrenceRemaining);

        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.Null(fromDb.DueDate);
        Assert.Equal(RecurrenceType.None, fromDb.Recurrence);
        Assert.Null(fromDb.RecurrenceRemaining);
    }

    // ---- T3.4 行 VM ----

    private static TodoItemViewModel NewRowVm(TodoRepository repo, TodoItem item)
        => new(repo, item, Array.Empty<SidebarItemViewModel>());

    [Fact]
    public void ApplyRecurrence_未设截止日期_整组拒绝并提示()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var item = repo.Items.Single();
        var vm = NewRowVm(repo, item);

        vm.SelectedRecurrence = vm.RecurrenceOptions.First(o => o.Value == RecurrenceType.Weekly);
        vm.ApplyRecurrenceCommand.Execute(null);

        Assert.Contains("截止日期", vm.RecurrenceError);
        Assert.Equal(RecurrenceType.None, item.Recurrence);
    }

    [Fact]
    public void ApplyRecurrence_非法间隔或次数_提示且不落库()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var item = repo.Items.Single();
        repo.SetDueDate(item.Id, DateTime.Today.AddDays(7));
        var vm = NewRowVm(repo, item);

        vm.SelectedRecurrence = vm.RecurrenceOptions.First(o => o.Value == RecurrenceType.Daily);
        vm.RecurrenceIntervalText = "0";
        vm.ApplyRecurrenceCommand.Execute(null);
        Assert.NotEmpty(vm.RecurrenceError);
        Assert.Equal(RecurrenceType.None, item.Recurrence);

        vm.RecurrenceIntervalText = "1";
        vm.RecurrenceRemainingText = "0";
        vm.ApplyRecurrenceCommand.Execute(null);
        Assert.NotEmpty(vm.RecurrenceError);
        Assert.Equal(RecurrenceType.None, item.Recurrence);
    }

    [Fact]
    public void ApplyRecurrence_合法输入_写库且回填摘要()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var item = repo.Items.Single();
        repo.SetDueDate(item.Id, DateTime.Today.AddDays(7));
        var vm = NewRowVm(repo, item);

        vm.SelectedRecurrence = vm.RecurrenceOptions.First(o => o.Value == RecurrenceType.Weekly);
        vm.RecurrenceIntervalText = "2";
        vm.RecurrenceRemainingText = "3";
        vm.ApplyRecurrenceCommand.Execute(null);

        Assert.Equal("", vm.RecurrenceError);
        Assert.Equal(RecurrenceType.Weekly, item.Recurrence);
        Assert.Equal(2, item.RecurrenceInterval);
        Assert.Equal(3, item.RecurrenceRemaining);
        Assert.Equal("每 2 周 · 剩 3 次", vm.RecurrenceText);

        // 权威源交叉验证。
        var fromDb = new DatabaseService(_dbPath).LoadAll().Single();
        Assert.Equal(RecurrenceType.Weekly, fromDb.Recurrence);
        Assert.Equal(2, fromDb.RecurrenceInterval);
    }

    [Fact]
    public void ApplyRecurrence_次数留空_为无限()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var item = repo.Items.Single();
        repo.SetDueDate(item.Id, DateTime.Today.AddDays(3));
        var vm = NewRowVm(repo, item);

        vm.SelectedRecurrence = vm.RecurrenceOptions.First(o => o.Value == RecurrenceType.Daily);
        vm.RecurrenceIntervalText = "1";
        vm.RecurrenceRemainingText = "  ";
        vm.ApplyRecurrenceCommand.Execute(null);

        Assert.Equal("", vm.RecurrenceError);
        Assert.Null(item.RecurrenceRemaining);
        Assert.Equal("每天", vm.RecurrenceText);
    }

    [Fact(Skip = "[DEFERRED 2026-10-18] 阶段三滚动分支已注释停用（TodoRepository.SetCompleted）；重新启用后移除 Skip，并排查'完成后日期未推进'的实时链路缺陷")]
    public void 仓库落库回调_摘要随投影滚动同步刷新()
    {
        var repo = NewRepository();
        repo.Add("任务A");
        var item = repo.Items.Single();
        repo.SetDueDate(item.Id, DateTime.Today);
        repo.SetRecurrence(item.Id, RecurrenceType.Daily, 1, 2);
        var vm = NewRowVm(repo, item);
        Assert.Equal("每天 · 剩 2 次", vm.RecurrenceText);

        // 完成滚动：剩余 2 → 1，行 VM 摘要应随投影事件刷新。
        repo.SetCompleted(item.Id, true);

        Assert.False(item.IsCompleted);
        Assert.Equal(1, item.RecurrenceRemaining);
        Assert.Equal("每天 · 剩 1 次", vm.RecurrenceText);
        Assert.Equal(DateTime.Today.AddDays(1), item.DueDate);
    }
}
