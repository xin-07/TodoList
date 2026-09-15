using System;
using System.IO;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using TodoList.ViewModels;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// B6 时间依赖注入的回归测试：到期/过期文案与占位提示由注入时钟决定，不再依赖真实系统时间。
/// </summary>
public class B6ClockInjectionTests : IDisposable
{
    private readonly string _dbPath;

    public B6ClockInjectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "todolist_b6_" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { }
        }
    }

    /// <summary>固定时钟为 2026-01-15 10:00 的 VM 工厂。</summary>
    private TodoItemViewModel NewDueVm(DateTime? dueDate, bool isCompleted = false)
    {
        var repo = new TodoRepository(new DatabaseService(_dbPath));
        var choices = new SidebarItemViewModel[]
        {
            new SidebarItemViewModel(repo, SidebarKind.Uncategorized),
        };
        var item = new TodoItem
        {
            DueDate = dueDate,
            IsCompleted = isCompleted,
        };
        var fixedNow = new DateTime(2026, 1, 15, 10, 0, 0);
        return new TodoItemViewModel(repo, item, choices, () => fixedNow);
    }

    [Fact]
    public void RefreshDue_过去未完成_标为已过期()
    {
        var vm = NewDueVm(new DateTime(2026, 1, 14, 20, 0, 0));
        Assert.Equal("已过期", vm.DueText);
        Assert.True(vm.IsOverdue);
        Assert.False(vm.IsDueToday);
    }

    [Fact]
    public void RefreshDue_今天到期_标为今天到期()
    {
        var vm = NewDueVm(new DateTime(2026, 1, 15, 0, 0, 0));
        Assert.Equal("今天到期", vm.DueText);
        Assert.True(vm.IsDueToday);
        Assert.False(vm.IsOverdue);
    }

    [Fact]
    public void RefreshDue_未来日期_不显示文案()
    {
        var vm = NewDueVm(new DateTime(2026, 1, 16, 9, 0, 0));
        Assert.Equal("", vm.DueText);
        Assert.False(vm.IsOverdue);
        Assert.False(vm.IsDueToday);
    }

    [Fact]
    public void RefreshDue_已完成的任务_不标过期()
    {
        var vm = NewDueVm(new DateTime(2026, 1, 14, 20, 0, 0), isCompleted: true);
        Assert.Equal("", vm.DueText);
        Assert.False(vm.IsOverdue);
    }

    [Fact]
    public void RefreshDue_无截止日期_清空文案与标记()
    {
        var vm = NewDueVm(null);
        Assert.Equal("", vm.DueText);
        Assert.False(vm.IsOverdue);
        Assert.False(vm.IsDueToday);
    }

    [Fact]
    public void DueDatePlaceholder_使用注入时钟的当天()
    {
        var vm = NewDueVm(null);
        Assert.Equal("2026-01-15", vm.DueDatePlaceholder);
    }

    [Fact]
    public void DueDateSelectedDate_读取与修改_落库与通知正常()
    {
        var repo = new TodoRepository(new DatabaseService(_dbPath));
        repo.Add("测试任务");
        var item = repo.Items.First();
        var choices = new SidebarItemViewModel[]
        {
            new SidebarItemViewModel(repo, SidebarKind.Uncategorized),
        };
        var fixedNow = new DateTime(2026, 1, 15, 10, 0, 0);
        var vm = new TodoItemViewModel(repo, item, choices, () => fixedNow);

        Assert.Null(vm.DueDateSelectedDate);

        var targetDate = new DateTime(2026, 9, 20);
        vm.DueDateSelectedDate = targetDate;

        Assert.Equal(targetDate, vm.DueDateSelectedDate);
        Assert.Equal(targetDate, item.DueDate);
    }

    [Fact]
    public void DueDateSelectedDate_置为null_清空截止日期()
    {
        var repo = new TodoRepository(new DatabaseService(_dbPath));
        repo.Add("测试任务");
        var item = repo.Items.First();
        var initialDate = new DateTime(2026, 9, 20);
        repo.SetDueDate(item.Id, initialDate);

        var choices = new SidebarItemViewModel[]
        {
            new SidebarItemViewModel(repo, SidebarKind.Uncategorized),
        };
        var fixedNow = new DateTime(2026, 1, 15, 10, 0, 0);
        var vm = new TodoItemViewModel(repo, item, choices, () => fixedNow);

        Assert.Equal(initialDate, vm.DueDateSelectedDate);

        vm.DueDateSelectedDate = null;
        Assert.Null(vm.DueDateSelectedDate);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void DueDateDisplayText与ClearCommand_测试()
    {
        var repo = new TodoRepository(new DatabaseService(_dbPath));
        repo.Add("测试任务");
        var item = repo.Items.First();
        var choices = new SidebarItemViewModel[]
        {
            new SidebarItemViewModel(repo, SidebarKind.Uncategorized),
        };
        var fixedNow = new DateTime(2026, 1, 15, 10, 0, 0);
        var vm = new TodoItemViewModel(repo, item, choices, () => fixedNow);

        Assert.False(vm.IsDueDateSet);
        Assert.Equal("2026-01-15", vm.DueDateDisplayText);

        var date = new DateTime(2026, 9, 20);
        vm.DueDateSelectedDate = date;

        Assert.True(vm.IsDueDateSet);
        Assert.Equal("2026-09-20", vm.DueDateDisplayText);

        vm.ClearDueDateCommand.Execute(null);

        Assert.False(vm.IsDueDateSet);
        Assert.Equal("2026-01-15", vm.DueDateDisplayText);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void DisplayDate_按年与按月翻动命令_正常更新()
    {
        var repo = new TodoRepository(new DatabaseService(_dbPath));
        repo.Add("测试任务");
        var item = repo.Items.First();
        var choices = new SidebarItemViewModel[]
        {
            new SidebarItemViewModel(repo, SidebarKind.Uncategorized),
        };
        var fixedNow = new DateTime(2026, 1, 15, 10, 0, 0);
        var vm = new TodoItemViewModel(repo, item, choices, () => fixedNow);

        Assert.Equal(new DateTime(2026, 1, 15), vm.DisplayDate);
        Assert.Equal("2026年1月", vm.DisplayDateHeader);

        vm.NextYearCommand.Execute(null);
        Assert.Equal(new DateTime(2027, 1, 15), vm.DisplayDate);
        Assert.Equal("2027年1月", vm.DisplayDateHeader);

        vm.PrevYearCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 1, 15), vm.DisplayDate);

        vm.NextMonthCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 2, 15), vm.DisplayDate);
        Assert.Equal("2026年2月", vm.DisplayDateHeader);

        vm.PrevMonthCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 1, 15), vm.DisplayDate);
    }
}