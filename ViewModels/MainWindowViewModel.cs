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

/// <summary>删除文件夹确认事件参数：携带待删文件夹及其内置条目数。</summary>
public sealed class DeleteFolderConfirmEventArgs : EventArgs
{
    public DeleteFolderConfirmEventArgs(SidebarItemViewModel item)
    {
        Item = item;
    }

    public SidebarItemViewModel Item { get; }
}

/// <summary>
/// 主窗口 ViewModel。
///
/// 它不含权威数据，只消费仓库暴露的只读投影：
///  - 增删改一律调用仓库；
///  - 展示列表 <see cref="DisplayItems"/> 是按当前视图(全部/文件夹/未归类)过滤、
///    再按优先级从高到低排序后的只读投影，并叠加标题搜索。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    /// <summary>边栏三种视图的稳定 Key。</summary>
    public const string KeyAll = "all";
    public const string KeyUncategorized = "none";

    private readonly ITodoRepository _repo;
    /// <summary>可注入时钟，默认系统当前时间；到期提醒扫描据此可测。</summary>
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, TodoItemViewModel> _vmById = new();
    private readonly Dictionary<string, SidebarItemViewModel> _folderSidebarById = new();
    /// <summary>上次重建边栏时的文件夹快照（Id+Name），用于判断是否需要重建。</summary>
    private List<(string Id, string Name)> _folderSnapshot = new();
    private readonly ObservableCollection<TodoItemViewModel> _display = new();
    private readonly ObservableCollection<TodoItemViewModel> _filterDisplay = new();
    private readonly ReadOnlyObservableCollection<TodoItemViewModel> _filterView;

    private readonly ObservableCollection<SidebarItemViewModel> _sidebarItems = new();
    private readonly ReadOnlyObservableCollection<SidebarItemViewModel> _sidebarView;

    private readonly ObservableCollection<SidebarItemViewModel> _folderChoices = new();
    private readonly ReadOnlyObservableCollection<SidebarItemViewModel> _folderChoicesView;

    private readonly HashSet<string> _alarmedIds = new();
    private readonly DispatcherTimer _alarmTimer;

    private string _newFolderName = "";
    private string _addFolderError = "";
    private bool _isNewFolderOpen;
    private SidebarItemViewModel? _selectedSidebar;

    private string _newTaskTitle = "";
    private string _addError = "";
    private int _totalCount;
    private int _completedCount;

    public MainWindowViewModel(ITodoRepository repo, Func<DateTime>? nowProvider = null)
    {
        _repo = repo;
        _now = nowProvider ?? (() => DateTime.Now);
        _filterView = new ReadOnlyObservableCollection<TodoItemViewModel>(_filterDisplay);
        _sidebarView = new ReadOnlyObservableCollection<SidebarItemViewModel>(_sidebarItems);
        _folderChoicesView = new ReadOnlyObservableCollection<SidebarItemViewModel>(_folderChoices);

        RebuildSidebar();
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
        ApplyFilter();

        AddTaskCommand = new RelayCommand(_ => AddTask());
        RemoveTaskCommand = new RelayCommand(p => RemoveTask(p as TodoItemViewModel));
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        ToggleNewFolderCommand = new RelayCommand(_ => IsNewFolderOpen = !IsNewFolderOpen);
        DeleteFolderCommand = new RelayCommand(p =>
        {
            if (p is SidebarItemViewModel item)
                RequestDeleteFolder(item);
        });

        // 到期提醒：轻量定时器周期扫描，早于/等于当前时间且未完成的未提醒任务触发系统通知。
        _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _alarmTimer.Tick += OnAlarmTick;
        _alarmTimer.Start();
    }

    /// <summary>未完成任务到期提醒事件（由 View 层绑定系统通知）。</summary>
    public event EventHandler<DueAlarmEventArgs>? DueAlarm;

    /// <summary>删除文件夹确认请求事件（由 View 层弹出确认框后回调）。</summary>
    public event EventHandler<DeleteFolderConfirmEventArgs>? DeleteFolderRequested;

    /// <summary>任务列表（只读投影，原样）。</summary>
    public ReadOnlyObservableCollection<TodoItem> Items => _repo.Items;

    /// <summary>排序并过滤后的展示列表：按当前视图过滤、优先级高→低排序，并叠加标题搜索。</summary>
    public ReadOnlyObservableCollection<TodoItemViewModel> DisplayItems => _filterView;

    /// <summary>左侧边栏项：全部任务 → 各文件夹 → 未归类。</summary>
    public ReadOnlyObservableCollection<SidebarItemViewModel> SidebarItems => _sidebarView;

    /// <summary>任务行"移到文件夹"下拉选项：未归类 + 全部文件夹。</summary>
    public ReadOnlyObservableCollection<SidebarItemViewModel> FolderChoices => _folderChoicesView;

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set
        {
            if (SetProperty(ref _newTaskTitle, value))
                ApplyFilter();
        }
    }

    /// <summary>新增标题的校验错误提示。</summary>
    public string AddError
    {
        get => _addError;
        set => SetProperty(ref _addError, value);
    }

    public string NewFolderName
    {
        get => _newFolderName;
        set
        {
            if (SetProperty(ref _newFolderName, value))
                AddFolderError = "";
        }
    }

    public string AddFolderError
    {
        get => _addFolderError;
        set => SetProperty(ref _addFolderError, value);
    }

    /// <summary>新建文件夹输入区是否展开（由顶栏文件夹按钮切换）。</summary>
    public bool IsNewFolderOpen
    {
        get => _isNewFolderOpen;
        set => SetProperty(ref _isNewFolderOpen, value);
    }

    /// <summary>当前选中的边栏视图（双向绑定到 ListBox.SelectedItem）。</summary>
    public SidebarItemViewModel? SelectedSidebar
    {
        get => _selectedSidebar;
        set
        {
            if (ReferenceEquals(_selectedSidebar, value))
                return;
            _selectedSidebar = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int TotalCount => _totalCount;
    public int CompletedCount => _completedCount;

    public string StatsText => $"{_completedCount} / {_totalCount}";

    public ICommand AddTaskCommand { get; }
    public ICommand RemoveTaskCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ToggleNewFolderCommand { get; }

    /// <summary>删除文件夹命令：向 View 层发起确认请求（不直接删除）。</summary>
    public ICommand DeleteFolderCommand { get; }

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

    private void AddFolder()
    {
        var error = FolderName.Validate(NewFolderName);
        if (error is not null)
        {
            AddFolderError = error;
            return;
        }

        _repo.AddFolder(NewFolderName!.Trim());
        NewFolderName = "";
        AddFolderError = "";
        // 创建成功后收起命名输入区（点击其它区域/确认后均退出编辑）。
        IsNewFolderOpen = false;
    }

    /// <summary>
    /// 用户对某文件夹确认删除（View 层确认框回调）。
    /// 删除文件夹并连同其下条目一并删除；若当前正选中该文件夹则回退到"全部任务"。
    /// </summary>
    public void ConfirmDeleteFolder(SidebarItemViewModel item)
    {
        var wasSelected = ReferenceEquals(SelectedSidebar, item);
        _repo.DeleteFolder(item.Key);
        // 被删文件夹的边栏项/下拉项由 OnRepoChanged 重建。
        if (wasSelected)
            SelectedSidebar = _sidebarItems.FirstOrDefault(i => i.Key == KeyAll);
    }

    /// <summary>请求删除某文件夹（由 View 层调用以触发确认框）。</summary>
    public void RequestDeleteFolder(SidebarItemViewModel item)
        => DeleteFolderRequested?.Invoke(this, new DeleteFolderConfirmEventArgs(item));

    private void RemoveTask(TodoItemViewModel? vm)
    {
        if (vm is null)
            return;
        _repo.Remove(vm.Id);
    }

    private TodoItemViewModel CreateVm(TodoItem item)
    {
        var vm = new TodoItemViewModel(_repo, item, _folderChoicesView);
        _vmById[item.Id] = vm;
        return vm;
    }

    /// <summary>判断仓库中的文件夹集合相对上次重建是否变化（增删或改名）。</summary>
    private bool FoldersChanged()
    {
        var current = _repo.Folders.Select(f => (f.Id, f.Name)).ToList();
        if (current.Count != _folderSnapshot.Count)
            return true;
        for (var i = 0; i < current.Count; i++)
        {
            if (current[i] != _folderSnapshot[i])
                return true;
        }
        return false;
    }

    /// <summary>重建边栏与下拉选项，并刷新各文件夹内置条目数。</summary>
    private void RebuildSidebar()
    {
        // 下拉选项：未归类 → 各文件夹（行级归属选择）。
        _folderChoices.Clear();
        _folderChoices.Add(new SidebarItemViewModel(_repo, SidebarKind.Uncategorized));

        _sidebarItems.Clear();
        _sidebarItems.Add(new SidebarItemViewModel(_repo, SidebarKind.All));
        _folderSidebarById.Clear();
        var total = _repo.Items.Count;
        foreach (var f in _repo.Folders)
        {
            var count = total == 0 ? 0 : _repo.Items.Count(t => t.FolderId == f.Id);
            var item = new SidebarItemViewModel(_repo, SidebarKind.Folder, f);
            item.ItemCount = count;
            _sidebarItems.Add(item);
            _folderSidebarById[f.Id] = item;
            _folderChoices.Add(item);
        }
        var unassigned = total == 0 ? 0 : _repo.Items.Count(t => t.FolderId is null);
        _sidebarItems.Add(new SidebarItemViewModel(_repo, SidebarKind.Uncategorized));
        _sidebarItems[^1].ItemCount = unassigned;

        // 刷新"全部任务"计数。
        _sidebarItems[0].ItemCount = total;

        // 记录本次重建后的文件夹快照，供 FoldersChanged 判断。
        _folderSnapshot = _repo.Folders.Select(f => (f.Id, f.Name)).ToList();

        // 若当前选中项已失效（文件夹被删）则回退，否则保持。
        var preserve = _selectedSidebar?.Key;
        if (preserve is not null)
            SelectedSidebar = _sidebarItems.FirstOrDefault(i => i.Key == preserve);
        if (SelectedSidebar is null)
            SelectedSidebar = _sidebarItems.FirstOrDefault(i => i.Key == KeyAll);
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
        ApplyFilter();
    }

    private void OnRepoChanged()
    {
        RefreshStats();
        CleanAlarmedIds();
        // 仅当文件夹集合变化（增删/改名）时才重建边栏与下拉选项；
        // 否则只刷新计数。行内下拉选中会经 SelectedFolder 写库并同步走到这里，
        // 若无条件重建会清空该下拉自己的 ItemsSource，重入导致崩溃。
        if (FoldersChanged())
            RebuildSidebar();
        else
            RefreshSidebarCounts();
        Reorder();
        ApplyFilter();
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

    /// <summary>
    /// 重建过滤后的展示列表：叠加"标题搜索 + 当前视图（全部/文件夹/未归类）"两层过滤。
    /// 采用增量 diff：先移除不再命中的项，再按目标顺序补齐/校正，整体不复建。
    /// 避免在全量重建下于 ComboBox 选中回调链内触发集合重置导致重入崩溃。
    /// 空白搜索与"全部"视图时不额外过滤。
    /// </summary>
    private void ApplyFilter()
    {
        var query = _newTaskTitle.Trim();
        var activeKey = SelectedSidebar?.Key;

        // 目标序列：当前全部项过滤出符合搜索与视图的子序列（保持原相对顺序）。
        var target = new List<TodoItemViewModel>(_display.Count);
        foreach (var vm in _display)
        {
            if (query.Length != 0
                && !vm.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            if (activeKey is not null && activeKey != KeyAll && !IsInView(vm.Item, activeKey))
                continue;

            target.Add(vm);
        }

        // 增删均基于引用相等（TodoItemViewModel 未重载 Equals），稳定。
        // 1) 移除不再命中的项。
        for (var i = _filterDisplay.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(_filterDisplay[i]))
                _filterDisplay.RemoveAt(i);
        }

        // 2) 校正顺序：使 _filterDisplay 与 target 完全一致（缺失项补入，乱序者移动到位）。
        for (var i = 0; i < target.Count; i++)
        {
            var wanted = target[i];
            var cur = _filterDisplay.IndexOf(wanted);
            if (cur == -1)
                _filterDisplay.Insert(i, wanted);
            else if (cur != i)
                _filterDisplay.Move(cur, i);
        }
    }

    private static bool IsInView(TodoItem item, string key)
        => key == KeyUncategorized ? item.FolderId is null : item.FolderId == key;

    private void ScanDueAlarms()
    {
        var now = _now();
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
        if (sender is not TodoItem t)
            return;

        if (e.PropertyName == nameof(TodoItem.IsCompleted))
        {
            // 投影完成状态变化时，写回权威源(仓库→SQLite)。
            _repo.SetCompleted(t.Id, t.IsCompleted);
        }
        else if (e.PropertyName == nameof(TodoItem.FolderId))
        {
            // 条目归属变化：刷新边栏各文件夹内置条目数。
            RefreshSidebarCounts();
        }
    }

    private void RefreshSidebarCounts()
    {
        var total = _repo.Items.Count;
        if (_sidebarItems.Count == 0)
            return;
        _sidebarItems[0].ItemCount = total;
        foreach (var f in _repo.Folders)
        {
            if (_folderSidebarById.TryGetValue(f.Id, out var item))
                item.ItemCount = total == 0 ? 0 : _repo.Items.Count(t => t.FolderId == f.Id);
        }
        foreach (var item in _sidebarItems)
            if (item.Kind == SidebarKind.Uncategorized)
                item.ItemCount = total == 0 ? 0 : _repo.Items.Count(t => t.FolderId is null);
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