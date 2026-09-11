using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using TodoList.Models;
using TodoList.Repositories;

namespace TodoList.ViewModels;

/// <summary>
/// 主窗口 ViewModel。
///
/// 它不含权威数据，只消费仓库暴露的只读投影：
///  - 增删改一律调用仓库；
///  - 单个任务完成状态由 UI 勾选触发 SetCompleted 写入仓库。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly ITodoRepository _repo;
    private string _newTaskTitle = "";
    private int _totalCount;
    private int _completedCount;

    public MainWindowViewModel(ITodoRepository repo)
    {
        _repo = repo;
        RefreshStats();

        // 监听权威源的任何变更，派生数据(统计)随之刷新。
        _repo.Changed += RefreshStats;

        // 新增任务进城后，为其完成状态变化建立"写回仓库"的桥接。
        if (_repo.Items is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnCollectionChanged;
            SubscribeToAll(_repo.Items);
        }

        AddTaskCommand = new RelayCommand(_ => AddTask());
        RemoveTaskCommand = new RelayCommand(p => RemoveTask(p as TodoItem));
    }

    /// <summary>任务列表（只读投影）。</summary>
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<TodoItem> Items => _repo.Items;

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set => SetProperty(ref _newTaskTitle, value);
    }

    public int TotalCount => _totalCount;
    public int CompletedCount => _completedCount;

    public string StatsText => $"{_completedCount} / {_totalCount}";

    public ICommand AddTaskCommand { get; }
    public ICommand RemoveTaskCommand { get; }

    private void AddTask()
    {
        var title = NewTaskTitle?.Trim();
        if (string.IsNullOrEmpty(title))
            return;
        _repo.Add(title);
        NewTaskTitle = "";
    }

    private void RemoveTask(TodoItem? item)
    {
        if (item is null)
            return;
        _repo.Remove(item.Id);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
                if (item is TodoItem t)
                    t.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void SubscribeToAll(System.Collections.IEnumerable items)
    {
        foreach (var item in items)
            if (item is TodoItem t)
                t.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
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