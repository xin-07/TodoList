using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using TodoList.Database;
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

/// <summary>状态筛选下拉的单个选项。</summary>
public sealed record StatusOption(string Key, string Label);

/// <summary>优先级筛选下拉的单个选项（Value 为 null 表示全部）。</summary>
public sealed record PriorityFilterOption(TaskPriority? Value, string Label);

/// <summary>排序方式下拉的单个选项。</summary>
public sealed record SortOption(string Key, string Label);

/// <summary>主题下拉的单个选项（阶段六 T6.2）。</summary>
public sealed record ThemeOption(UiTheme Value, string Label);

/// <summary>近 14 天单日完成量条（阶段七统计弹层）：BarWidth 为按期内最大值归一的像素宽。</summary>
public sealed record CompletionBar(string Label, string CountText, double BarWidth);

/// <summary>
/// 主窗口 ViewModel。
///
/// 它不含权威数据，只消费仓库暴露的只读投影：
///  - 增删改一律调用仓库；
///  - 展示列表 <see cref="DisplayItems"/> 是按当前视图(全部/文件夹/未归类)、
///    状态与优先级筛选、标题搜索过滤后，按用户所选排序方式排序的只读投影。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    /// <summary>边栏三种视图的稳定 Key。</summary>
    public const string KeyAll = "all";
    public const string KeyUncategorized = "none";

    /// <summary>状态筛选的稳定 Key。</summary>
    public const string StatusAll = "all";
    public const string StatusIncomplete = "incomplete";
    public const string StatusCompleted = "completed";

    /// <summary>排序方式的稳定 Key。</summary>
    public const string SortPriority = "priority";
    public const string SortDueDate = "due";
    public const string SortCreatedAt = "created";

    /// <summary>状态下拉选项：全部 / 未完成 / 已完成（单一权威定义，经实例属性给 XAML/测试）。</summary>
    private static readonly IReadOnlyList<StatusOption> _statusOptions = new[]
    {
        new StatusOption(StatusAll, "全部"),
        new StatusOption(StatusIncomplete, "未完成"),
        new StatusOption(StatusCompleted, "已完成"),
    };

    /// <summary>优先级筛选下拉选项：全部 + 四档（单一权威定义）。</summary>
    private static readonly IReadOnlyList<PriorityFilterOption> _priorityFilterOptions = new[]
    {
        new PriorityFilterOption(null, "全部"),
        new PriorityFilterOption(TaskPriority.High, "高"),
        new PriorityFilterOption(TaskPriority.Medium, "中"),
        new PriorityFilterOption(TaskPriority.Low, "低"),
        new PriorityFilterOption(TaskPriority.None, "无"),
    };

    /// <summary>排序方式下拉选项（单一权威定义）。</summary>
    private static readonly IReadOnlyList<SortOption> _sortOptions = new[]
    {
        new SortOption(SortPriority, "优先级"),
        new SortOption(SortDueDate, "截止日期"),
        new SortOption(SortCreatedAt, "创建时间"),
    };

    /// <summary>主题下拉选项（单一权威定义，阶段六）。</summary>
    private static readonly IReadOnlyList<ThemeOption> _themeOptions = new[]
    {
        new ThemeOption(UiTheme.System, "跟随系统"),
        new ThemeOption(UiTheme.Light, "浅色"),
        new ThemeOption(UiTheme.Dark, "深色"),
    };

    private readonly ITodoRepository _repo;
    /// <summary>可注入时钟，默认系统当前时间；到期提醒扫描据此可测。</summary>
    private readonly Func<DateTime> _now;
    /// <summary>
    /// 可注入的主题应用回调（阶段六）：输入目标主题，完成"切换 Avalonia ThemeVariant + 写 config.json"。
    /// 由 App 装配；缺省（测试）时主题下拉只更新本地选择，不落盘。
    /// </summary>
    private readonly Func<UiTheme, bool>? _applyTheme;
    private ThemeOption _selectedTheme;
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
    private int _completedToday;
    private int _completedThisWeek;
    private int _completedThisMonth;
    private int _completionRate;
    private IReadOnlyList<CompletionBar> _recentBars = Array.Empty<CompletionBar>();
    private string _backupMessage = "";

    private StatusOption _statusFilter = _statusOptions[0];
    private PriorityFilterOption _priorityFilter = _priorityFilterOptions[0];
    private SortOption _sortBy = _sortOptions[0];
    /// <summary>整库备份能力（阶段七）：App 注入 DatabaseService；缺省（测试）时备份命令给出不可用提示。</summary>
    private readonly IDatabaseBackup? _backup;

    public MainWindowViewModel(ITodoRepository repo, Func<UiTheme, bool>? applyTheme = null, UiTheme? initialTheme = null,
        Func<DateTime>? nowProvider = null, IDatabaseBackup? backup = null)
    {
        _repo = repo;
        _now = nowProvider ?? (() => DateTime.Now);
        _applyTheme = applyTheme;
        _backup = backup;
        _selectedTheme = MatchTheme(initialTheme ?? UiTheme.System);
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
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());

        // 备份（阶段七）：VM 不触碰文件对话框，仅发事件；View 弹对话框后调用 ExportBackup/ImportBackup。
        ExportBackupCommand = new RelayCommand(_ => ExportBackupRequested?.Invoke(this, EventArgs.Empty));
        ImportBackupCommand = new RelayCommand(_ => ImportBackupRequested?.Invoke(this, EventArgs.Empty));

        // 到期提醒：轻量定时器周期扫描，早于/等于当前时间且未完成的未提醒任务触发系统通知。
        _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _alarmTimer.Tick += OnAlarmTick;
        _alarmTimer.Start();
    }

    /// <summary>未完成任务到期提醒事件（由 View 层绑定系统通知）。</summary>
    public event EventHandler<DueAlarmEventArgs>? DueAlarm;

    /// <summary>删除文件夹确认请求事件（由 View 层弹出确认框后回调）。</summary>
    public event EventHandler<DeleteFolderConfirmEventArgs>? DeleteFolderRequested;

    /// <summary>导出备份请求事件（由 View 层弹保存对话框，选定路径后调用 <see cref="ExportBackup"/>）。</summary>
    public event EventHandler? ExportBackupRequested;

    /// <summary>导入备份请求事件（由 View 层弹打开对话框并确认覆盖后调用 <see cref="ImportBackup"/>）。</summary>
    public event EventHandler? ImportBackupRequested;

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

    /// <summary>状态筛选下拉选项（全部/未完成/已完成）。</summary>
    public IReadOnlyList<StatusOption> StatusFilterOptions => _statusOptions;

    /// <summary>当前状态筛选（下拉双向绑定，变更后重跑过滤；默认"全部"）。</summary>
    public StatusOption StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (value is null || _statusFilter == value)
                return;
            _statusFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>优先级筛选下拉选项。</summary>
    public IReadOnlyList<PriorityFilterOption> PriorityFilterOptions => _priorityFilterOptions;

    /// <summary>当前优先级筛选（下拉双向绑定；Value 为 null 表示全部，变更后重跑过滤）。</summary>
    public PriorityFilterOption PriorityFilter
    {
        get => _priorityFilter;
        set
        {
            if (value is null || _priorityFilter == value)
                return;
            _priorityFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>排序方式下拉选项。</summary>
    public IReadOnlyList<SortOption> SortOptions => _sortOptions;

    /// <summary>当前排序方式（下拉双向绑定；变更后按新方向重排并刷新过滤视图）。</summary>
    public SortOption SortBy
    {
        get => _sortBy;
        set
        {
            if (value is null || _sortBy == value)
                return;
            _sortBy = value;
            OnPropertyChanged();
            Reorder();
            ApplyFilter();
        }
    }

    /// <summary>主题下拉选项。</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions => _themeOptions;

    /// <summary>
    /// 当前主题选择（下拉双向绑定，阶段六 T6.2）。
    /// 变更时经注入回调执行"切 Avalonia ThemeVariant + 写 config.json（best-effort）"；
    /// 回调缺省（测试/无装配）时仅更新本地选择，不落盘。
    /// </summary>
    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || _selectedTheme == value)
                return;
            _selectedTheme = value;
            OnPropertyChanged();
            _applyTheme?.Invoke(value.Value);
        }
    }

    /// <summary>把主题枚举映射到下拉选项实例（未知值兜底"跟随系统"）。</summary>
    private static ThemeOption MatchTheme(UiTheme value)
        => _themeOptions.FirstOrDefault(o => o.Value == value) ?? _themeOptions[0];

    /// <summary>展示列表是否为空（驱动空状态引导可见性；变更通知由 ApplyFilter 统一发出）。</summary>
    public bool IsEmpty => _filterDisplay.Count == 0;

    /// <summary>
    /// 是否有生效中的标题搜索或状态/优先级筛选（驱动"清除筛选"按钮可见性）。
    /// 边栏视图选择与排序方式不计入：视图为空有专属空状态文案，清除筛选也保留视图与排序。
    /// </summary>
    public bool HasActiveFilter =>
        _newTaskTitle.Trim().Length != 0
        || _statusFilter.Key != StatusAll
        || _priorityFilter.Value is not null;

    /// <summary>空状态引导文案，按"库内无任务 / 有任务但筛选无匹配 / 有任务但当前视图为空"三态区分。</summary>
    public string EmptyStateText =>
        _totalCount == 0
            ? "暂无任务，输入标题后回车创建第一条"
            : HasActiveFilter
                ? "没有符合筛选条件的任务"
                : "此视图暂无任务";

    public int TotalCount => _totalCount;
    public int CompletedCount => _completedCount;

    public string StatsText => $"{_completedCount} / {_totalCount}";

    /// <summary>今日完成数（全库口径，按 completed_at 当地日期；旧库 NULL 不计入）。</summary>
    public int CompletedToday => _completedToday;

    /// <summary>本周完成数（周一起，含今天）。</summary>
    public int CompletedThisWeek => _completedThisWeek;

    /// <summary>本月完成数（自然月）。</summary>
    public int CompletedThisMonth => _completedThisMonth;

    /// <summary>完成率百分比（0-100；总任务为 0 时为 0）。</summary>
    public int CompletionRate => _completionRate;

    /// <summary>近 14 天每日完成量条（今天在最右，天内最大值归一宽度）。</summary>
    public IReadOnlyList<CompletionBar> RecentCompletionBars => _recentBars;

    /// <summary>最近一次备份操作的结果提示（空=不显示）。</summary>
    public string BackupMessage
    {
        get => _backupMessage;
        set => SetProperty(ref _backupMessage, value);
    }

    public ICommand AddTaskCommand { get; }
    public ICommand RemoveTaskCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ToggleNewFolderCommand { get; }

    /// <summary>删除文件夹命令：向 View 层发起确认请求（不直接删除）。</summary>
    public ICommand DeleteFolderCommand { get; }

    /// <summary>清除筛选命令：重置状态、优先级与标题搜索（保留排序方式与边栏视图）。</summary>
    public ICommand ClearFiltersCommand { get; }

    /// <summary>导出备份命令：向 View 层发起请求（弹保存对话框后回调 <see cref="ExportBackup"/>）。</summary>
    public ICommand ExportBackupCommand { get; }

    /// <summary>导入备份命令：向 View 层发起请求（弹打开对话框 + 覆盖确认后回调 <see cref="ImportBackup"/>）。</summary>
    public ICommand ImportBackupCommand { get; }

    private void AddTask()
    {
        var error = TaskTitle.Validate(NewTaskTitle);
        if (error is not null)
        {
            AddError = error;
            return;
        }

        // 归属：边栏选中具体文件夹时归入该文件夹；"全部任务/未归类"视作未选择，归入未归类。
        var folderId = SelectedSidebar?.Kind == SidebarKind.Folder ? SelectedSidebar.Key : null;
        _repo.Add(NewTaskTitle!.Trim(), folderId);
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
    /// 清除筛选：重置状态、优先级与标题搜索，保留排序方式与边栏视图。
    /// 经属性 setter 赋值，各变更通知与过滤重跑路径与手选下拉一致；已是默认值时自动跳过。
    /// </summary>
    private void ClearFilters()
    {
        StatusFilter = _statusOptions[0];
        PriorityFilter = _priorityFilterOptions[0];
        NewTaskTitle = "";
        // 输入框即搜索框，一并清掉残留的新增校验错误。
        AddError = "";
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

        // _folderChoices 刚经历 Clear+重建，行内下拉的选中项在 Clear 时被置空且不会自动恢复；
        // 通知各行重新读取 SelectedFolder（按 Key 匹配新实例），避免下拉显示空白。
        foreach (var vm in _vmById.Values)
            vm.RefreshFolderSelection();
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

    /// <summary>
    /// 将展示列表重排为两段：未完成区按当前排序方式（优先级高→低默认 / 截止近→远无日期最后 /
    /// 创建时间新→旧），已完成区恒定沉底、段内按创建时间新→旧——全局恒定显示规则，
    /// 不受排序下拉影响（重启加载、视图切换、搜索筛选、排序切换下均成立）；
    /// 取消勾选则按当前排序规则归位未完成区。
    /// 仅重排权威展示序列 <see cref="_display"/>，过滤视图顺序由随后调用的 ApplyFilter 对齐。
    /// </summary>
    private void Reorder()
    {
        if (_display.Count < 2)
            return;

        var incomplete = _sortBy.Key switch
        {
            SortCreatedAt => _display
                .Where(v => !v.Item.IsCompleted)
                .OrderByDescending(v => v.Item.CreatedAt)
                .ToList(),
            SortDueDate => _display
                .Where(v => !v.Item.IsCompleted)
                .OrderBy(v => v.Item.DueDate ?? DateTime.MaxValue)
                .ThenBy(v => v.Item.CreatedAt)
                .ToList(),
            _ => _display
                .Where(v => !v.Item.IsCompleted)
                .OrderByDescending(v => v.PriorityRank)
                .ThenBy(v => v.Item.CreatedAt)
                .ToList(),
        };

        // 已完成区：恒定沉底，段内按创建时间新→旧（与排序下拉选择无关）。
        var completed = _display
            .Where(v => v.Item.IsCompleted)
            .OrderByDescending(v => v.Item.CreatedAt);

        var target = incomplete.Concat(completed).ToList();

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
    /// 重建过滤后的展示列表：依次叠加"标题搜索 + 状态/优先级筛选 + 当前视图（全部/文件夹/未归类）"过滤。
    /// 采用增量 diff：先移除不再命中的项，再按目标顺序补齐/校正，整体不复建。
    /// 避免在全量重建下于 ComboBox 选中回调链内触发集合重置导致重入崩溃。
    /// 空白搜索、状态"全部"、优先级"全部"与"全部"视图时不额外过滤。
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

            if (_statusFilter.Key == StatusIncomplete && vm.Item.IsCompleted)
                continue;
            if (_statusFilter.Key == StatusCompleted && !vm.Item.IsCompleted)
                continue;
            if (_priorityFilter.Value is not null && vm.Item.Priority != _priorityFilter.Value.Value)
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

        // 空状态派生展示可能随过滤结果变化，统一在此处广播（可见性随本次过滤实时更新）。
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(EmptyStateText));
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

    /// <summary>
    /// 重算全库口径统计（阶段七）：总数/完成数之外，增加完成率、今日/本周/本月完成量与近 14 天条形。
    /// 本周自周一起、本月为自然月，均依据注入时钟；completed_at 为 NULL（旧库历史完成）不计入日趋势。
    /// public：供统计弹层打开时刷新一次（时钟跨天/跨周后打开按钮即可取到新口径）。
    /// </summary>
    public void RefreshStats()
    {
        _totalCount = _repo.Items.Count;
        _completedCount = 0;
        _completedToday = 0;
        _completedThisWeek = 0;
        _completedThisMonth = 0;
        foreach (var t in _repo.Items)
        {
            if (!t.IsCompleted)
                continue;
            _completedCount++;
            if (t.CompletedAt is { } done)
            {
                var today = _now().Date;
                if (done.Date == today)
                    _completedToday++;
                var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                if (done.Date >= weekStart && done.Date < today.AddDays(1))
                    _completedThisWeek++;
                var monthStart = new DateTime(today.Year, today.Month, 1);
                if (done.Date >= monthStart && done.Date < monthStart.AddMonths(1))
                    _completedThisMonth++;
            }
        }
        _completionRate = _totalCount == 0 ? 0 : (int)Math.Round(100.0 * _completedCount / _totalCount);
        _recentBars = BuildRecentBars();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(CompletedToday));
        OnPropertyChanged(nameof(CompletedThisWeek));
        OnPropertyChanged(nameof(CompletedThisMonth));
        OnPropertyChanged(nameof(CompletionRate));
        OnPropertyChanged(nameof(RecentCompletionBars));
    }

    /// <summary>近 14 天（13 天前到今天）每日完成量条：宽度按期内最大值归一，无完成时为 0 宽。</summary>
    private List<CompletionBar> BuildRecentBars()
    {
        const int barMaxWidth = 120;
        var today = _now().Date;
        var days = Enumerable.Range(-13, 14).Select(offset => today.AddDays(offset)).ToList();
        var counts = days.Select(day => _repo.Items.Count(t => t.CompletedAt?.Date == day)).ToList();
        var max = counts.Count == 0 ? 0 : counts.Max();
        var bars = new List<CompletionBar>(days.Count);
        for (var i = 0; i < days.Count; i++)
        {
            bars.Add(new CompletionBar(
                days[i].ToString("M/d"),
                counts[i] == 0 ? "" : counts[i].ToString(),
                max == 0 ? 0 : barMaxWidth * counts[i] / max));
        }
        return bars;
    }

    /// <summary>执行导出备份（View 弹保存对话框、选定路径后调用）。null 路径=用户取消。</summary>
    public void ExportBackup(string? destinationPath)
    {
        if (destinationPath is null)
        {
            BackupMessage = "";
            return;
        }
        if (_backup is null)
        {
            BackupMessage = "备份功能不可用";
            return;
        }
        BackupMessage = _backup.ExportTo(destinationPath)
            ? "导出完成"
            : "导出失败（目标路径可能无权限或被占用）";
    }

    /// <summary>执行导入备份（View 弹打开对话框并确认覆盖后调用）。null 路径=用户取消。</summary>
    public void ImportBackup(string? sourcePath)
    {
        if (sourcePath is null)
        {
            BackupMessage = "";
            return;
        }
        if (_backup is null)
        {
            BackupMessage = "备份功能不可用";
            return;
        }
        // 内存投影不热重载：导入只替换库文件，重启后以新权威源载入（避免两份真相冲突）。
        BackupMessage = _backup.ImportFrom(sourcePath)
            ? "已导入，重启 TodoList 后生效"
            : "导入失败：所选文件不是有效的 TodoList 备份";
    }
}