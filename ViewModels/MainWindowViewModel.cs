using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using TodoList.Models;
using TodoList.Repositories;

namespace TodoList.ViewModels;

/// <summary>到期提醒事件参数。</summary>
public sealed class DueAlarmEventArgs : EventArgs
{
    public DueAlarmEventArgs(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
}

/// <summary>
/// 主窗口 ViewModel。
///
/// 它不含权威数据，只消费仓库暴露的只读投影：
///  - 增删改一律调用仓库；
///  - 单个任务完成状态由 UI 勾选触发 SetCompleted 写入仓库；
///  - 标题/优先级/截止日期由行 ViewModel 调用仓库写库。
///  - 展示列表 <see cref="DisplayItems"/> 是按优先级从高到低排序后的只读投影。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly ITodoRepository _repo;
    private readonly Dictionary<string, TodoItemViewModel> _vmById = new();
    private readonly ObservableCollection<TodoItemViewModel> _display = new();
    private readonly ReadOnlyObservableCollection<TodoItemViewModel> _displayView;
    private readonly HashSet<string> _alarmedIds = new();
    private readonly DispatcherTimer _alarmTimer;

    private string _newTaskTitle = "";
    private string _addError = "";
    private int _totalCount;
    private int _completedCount;

    public MainWindowViewModel(ITodoRepository repo)
    {
        _repo = repo;
        _displayView = new ReadOnlyObservableCollection<TodoItemViewModel>(_display);

        RefreshStats();
        _repo.Changed += OnRepoChanged;

        foreach (var t in _repo.Items)
        {
            t.PropertyChanged += OnItemPropertyChanged;
            _display.Add(CreateVm(t));
        }

        if (_repo.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += OnCollectionChanged;

        Reorder();

        AddTaskCommand = new RelayCommand(_ => AddTask());
        RemoveTaskCommand = new RelayCommand(p => RemoveTask(p as TodoItemViewModel));

        // 到期提醒：轻量定时器周期扫描，早于/等于当前时间且未完成的未提醒任务触发系统通知。
        _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _alarmTimer.Tick += OnAlarmTick;
        _alarmTimer.Start();
    }

    /// <summary>未完成任务到期提醒事件（由 View 层绑定系统通知）。</summary>
    public event EventHandler<DueAlarmEventArgs>? DueAlarm;

    /// <summary>任务列表（只读投影，原样）。</summary>
    public ReadOnlyObservableCollection<TodoItem> Items => _repo.Items;

    /// <summary>排序后的展示列表：按优先级高→低展示。</summary>
    public ReadOnlyObservableCollection<TodoItemViewModel> DisplayItems => _displayView;

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set => SetProperty(ref _newTaskTitle, value);
    }

    /// <summary>新增标题的校验错误提示。</summary>
    public string AddError
    {
        get => _addError;
        set => SetProperty(ref _addError, value);
    }

    public int TotalCount => _totalCount;
    public int CompletedCount => _completedCount;

    public string StatsText => $"{_completedCount} / {_totalCount}";

    public ICommand AddTaskCommand { get; }
    public ICommand RemoveTaskCommand { get; }

    private void AddTask()
    {
        var error = TaskTitle.Validate(NewTaskTitle);
        if (error is not null)
        {
            AddError = error;
            return;
        }

        _repo.Add(NewTaskTitle!.Trim());
        NewTaskTitle = "";
        AddError = "";
    }

    private void RemoveTask(TodoItemViewModel? vm)
    {
        if (vm is null)
            return;
        _repo.Remove(vm.Id);
    }

    private TodoItemViewModel CreateVm(TodoItem item)
    {
        var vm = new TodoItemViewModel(_repo, item);
        _vmById[item.Id] = vm;
        return vm;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var it in e.NewItems)
            {
                if (it is TodoItem t)
                {
                    t.PropertyChanged += OnItemPropertyChanged;
                    _display.Add(CreateVm(t));
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var it in e.OldItems)
            {
                if (it is TodoItem t)
                {
                    t.PropertyChanged -= OnItemPropertyChanged;
                    if (_vmById.Remove(t.Id, out var vm))
                        _display.Remove(vm);
                }
            }
        }

        Reorder();
    }

    private void OnRepoChanged()
    {
        RefreshStats();
        CleanAlarmedIds();
        Reorder();
    }

    /// <summary>按优先级从高到低（稳定）重排展示列表。</summary>
    private void Reorder()
    {
        if (_display.Count < 2)
            return;

        var target = _display
            .OrderByDescending(v => v.PriorityRank)
            .ThenBy(v => v.Item.CreatedAt)
            .ToList();

        var current = _display.ToList();
        for (var i = 0; i < target.Count; i++)
        {
            var wanted = target[i];
            var currentIndex = current.IndexOf(wanted);
            if (currentIndex != i)
            {
                _display.Move(currentIndex, i);
                current.RemoveAt(currentIndex);
                current.Insert(i, wanted);
            }
        }
    }

    private void OnAlarmTick(object? sender, EventArgs e)
    {
        foreach (var vm in _vmById.Values)
            vm.RefreshDue();
        ScanDueAlarms();
    }

    private void ScanDueAlarms()
    {
        var now = DateTime.Now;
        foreach (var vm in _vmById.Values)
        {
            var item = vm.Item;
            if (item.IsCompleted || !item.DueDate.HasValue || item.DueDate.Value > now)
                continue;

            if (_alarmedIds.Add(item.Id))
                DueAlarm?.Invoke(this, new DueAlarmEventArgs(item.Id, item.Title));
        }
    }

    /// <summary>清理已完成的或已删除任务的提醒记录，避免集合无限增长。</summary>
    private void CleanAlarmedIds()
    {
        if (_alarmedIds.Count == 0)
            return;

        var stale = new List<string>();
        foreach (var id in _alarmedIds)
        {
            if (!_vmById.TryGetValue(id, out var vm) || vm.Item.IsCompleted)
                stale.Add(id);
        }

        foreach (var id in stale)
            _alarmedIds.Remove(id);
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TodoItem.IsCompleted) || sender is not TodoItem t)
            return;
        // 投影完成状态变化时，写回权威源(仓库→SQLite)。
        _repo.SetCompleted(t.Id, t.IsCompleted);
    }

    private void RefreshStats()
    {
        _totalCount = _repo.Items.Count;
        _completedCount = 0;
        foreach (var t in _repo.Items)
            if (t.IsCompleted)
                _completedCount++;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(StatsText));
    }
}