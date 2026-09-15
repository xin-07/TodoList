using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TodoList.Models;
using TodoList.Repositories;

namespace TodoList.ViewModels;

/// <summary>优先级下拉的单个选项。</summary>
public sealed record PriorityOption(TaskPriority Value, string Label);

/// <summary>
/// 列表行的 ViewModel：承载行内编辑状态、优先级/截止日期等派生展示。
///
/// 它不持有权威数据，只包装仓库投影中的 <see cref="TodoItem"/>：
///  - 完成状态由 CheckBox 双向绑定到 TodoItem.IsCompleted，再由 MainWindowViewModel 写回仓库；
///  - 标题/优先级/截止日期的变更一律调用仓库（Rename/SetPriority/SetDueDate）完成写库。
/// </summary>
public sealed class TodoItemViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<PriorityOption> _priorityOptions = new[]
    {
        new PriorityOption(TaskPriority.None, "无"),
        new PriorityOption(TaskPriority.Low, "低"),
        new PriorityOption(TaskPriority.Medium, "中"),
        new PriorityOption(TaskPriority.High, "高"),
    };

    private readonly ITodoRepository _repo;
    private readonly TodoItem _item;
    private readonly IReadOnlyList<SidebarItemViewModel> _folderChoices;
    /// <summary>可注入时钟，默认系统当前时间；到期判断据此可测。</summary>
    private readonly Func<DateTime> _now;

    private bool _isEditing;
    private string _editText = "";
    private string _editError = "";
    private string _dueDateText = "";
    private string _dueDateError = "";
    private string _dueText = "";
    private bool _isOverdue;
    private bool _isDueToday;
    /// <summary>该行截止日期输入框当前是否聚焦（用户正在编辑）。定时器刷新时据此跳过覆盖输入内容。</summary>
    public bool IsDueDateEditing { get; set; }

    private DateTime _displayDate;

    public TodoItemViewModel(ITodoRepository repo, TodoItem item, IReadOnlyList<SidebarItemViewModel> folderChoices,
        Func<DateTime>? nowProvider = null)
    {
        _repo = repo;
        _item = item;
        _folderChoices = folderChoices;
        _now = nowProvider ?? (() => DateTime.Now);
        _item.PropertyChanged += OnItemPropertyChanged;
        _editText = item.Title;
        _dueDateText = item.DueDate.HasValue ? DueDate.ToDisplayString(item.DueDate.Value) : "";
        _displayDate = item.DueDate?.Date ?? _now().Date;
        RefreshDue();

        BeginEditCommand = new RelayCommand(_ => BeginEdit());
        SaveEditCommand = new RelayCommand(_ => CommitEdit());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        SetDueDateCommand = new RelayCommand(_ => ApplyDueDate());
        ClearDueDateCommand = new RelayCommand(_ => DueDateSelectedDate = null);
        PrevYearCommand = new RelayCommand(_ => DisplayDate = DisplayDate.AddYears(-1));
        NextYearCommand = new RelayCommand(_ => DisplayDate = DisplayDate.AddYears(1));
        PrevMonthCommand = new RelayCommand(_ => DisplayDate = DisplayDate.AddMonths(-1));
        NextMonthCommand = new RelayCommand(_ => DisplayDate = DisplayDate.AddMonths(1));
    }

    /// <summary>归属下拉选项：未归类 + 全部文件夹。未归类项 Key="none"。</summary>
    public IReadOnlyList<SidebarItemViewModel> FolderChoices => _folderChoices;

    /// <summary>
    /// 当前归属选择（下拉双向绑定）。选中的必须是 <see cref="_folderChoices"/> 中的同一实例；
    /// 变更时写回仓库（按 FolderId 归属；"未归类"=null）。
    /// </summary>
    public SidebarItemViewModel SelectedFolder
    {
        get
        {
            foreach (var c in _folderChoices)
            {
                var match = _item.FolderId is null
                    ? c.Kind == SidebarKind.Uncategorized
                    : c.Kind == SidebarKind.Folder && c.Key == _item.FolderId;
                if (match)
                    return c;
            }
            // 兜底回退到第一个（未归类），避免出现空白下拉。
            return _folderChoices.FirstOrDefault() ?? _folderChoices[0];
        }
        set
        {
            if (value is null)
                return;
            var folderId = value.Kind == SidebarKind.Folder ? value.Key : null;
            _repo.SetFolder(_item.Id, folderId);
        }
    }

    /// <summary>
    /// 归属选项集合重建后（新建/改名/删除文件夹），让下拉重新读取 <see cref="SelectedFolder"/>。
    /// 否则 ComboBox 在 Clear 时被置空选中，重建后不会自动恢复，表现为下拉空白。
    /// </summary>
    public void RefreshFolderSelection() => OnPropertyChanged(nameof(SelectedFolder));

    /// <summary>原始任务 Id。</summary>
    public string Id => _item.Id;

    /// <summary>被包装的仓库投影对象（仅供排序等只读使用）。</summary>
    public TodoItem Item => _item;

    /// <summary>标题。</summary>
    public string Title => _item.Title;

    /// <summary>完成状态：双向绑定到 TodoItem，由 MainWindowViewModel 写回仓库。</summary>
    public bool IsCompleted
    {
        get => _item.IsCompleted;
        set => _item.IsCompleted = value;
    }

    /// <summary>已完成时整行淡化（1=正常，0.55=完成置灰）。普通属性绑定对变更实时响应。</summary>
    public double RowOpacity => _item.IsCompleted ? 0.55 : 1.0;

    /// <summary>优先级下拉选项。</summary>
    public IReadOnlyList<PriorityOption> PriorityOptions => _priorityOptions;

    /// <summary>当前优先级（ComboBox 双向绑定，变更时写回仓库）。</summary>
    public PriorityOption Priority
    {
        get => _item.Priority switch
        {
            TaskPriority.High => _priorityOptions[3],
            TaskPriority.Medium => _priorityOptions[2],
            TaskPriority.Low => _priorityOptions[1],
            _ => _priorityOptions[0],
        };
        set
        {
            if (value is not null && value.Value != _item.Priority)
                _repo.SetPriority(_item.Id, value.Value);
        }
    }

    /// <summary>用于显示的高亮标签文本。</summary>
    public string PriorityLabel => Priority.Label;

    public bool IsHigh => _item.Priority == TaskPriority.High;
    public bool IsMedium => _item.Priority == TaskPriority.Medium;
    public bool IsLow => _item.Priority == TaskPriority.Low;
    public bool IsNone => _item.Priority == TaskPriority.None;

    /// <summary>排序键：优先级从高到低的数值（High&gt;Medium&gt;Low&gt;None）。</summary>
    public int PriorityRank => _item.Priority switch
    {
        TaskPriority.High => 3,
        TaskPriority.Medium => 2,
        TaskPriority.Low => 1,
        _ => 0,
    };

    // ---- 标题现场编辑 ----

    public bool IsEditing { get => _isEditing; private set => SetProperty(ref _isEditing, value); }

    public bool IsNotEditing => !_isEditing;

    public string EditText
    {
        get => _editText;
        set
        {
            if (SetProperty(ref _editText, value) && !string.IsNullOrWhiteSpace(value))
                EditError = "";
        }
    }

    public string EditError { get => _editError; private set => SetProperty(ref _editError, value); }

    // ---- 截止日期 ----

    /// <summary>截止日期（Calendar 双向绑定，选择变更时自动写库）。</summary>
    public DateTime? DueDateSelectedDate
    {
        get => _item.DueDate;
        set
        {
            var dateVal = value?.Date;
            if (_item.DueDate != dateVal)
            {
                _repo.SetDueDate(_item.Id, dateVal);
                if (dateVal.HasValue)
                    DisplayDate = dateVal.Value;
            }
        }
    }

    /// <summary>日历控件当前展示视角日期。</summary>
    public DateTime DisplayDate
    {
        get => _displayDate;
        set
        {
            if (SetProperty(ref _displayDate, value.Date))
                OnPropertyChanged(nameof(DisplayDateHeader));
        }
    }

    /// <summary>日历控件顶部年月标题（例如 "2026年5月"）。</summary>
    public string DisplayDateHeader => DisplayDate.ToString("yyyy年M月");

    /// <summary>截止日期选择框显示的文本：已设置显示实际日期，未设置显示占位提示。</summary>
    public string DueDateDisplayText => _item.DueDate.HasValue
        ? DueDate.ToDisplayString(_item.DueDate.Value)
        : DueDatePlaceholder;

    /// <summary>是否已设置截止日期（控制选择框文本样式与清空按钮可见性）。</summary>
    public bool IsDueDateSet => _item.DueDate.HasValue;

    public string DueDateText
    {
        get => _dueDateText;
        set
        {
            if (SetProperty(ref _dueDateText, value))
                DueDateError = "";
        }
    }

    public string DueDateError { get => _dueDateError; private set => SetProperty(ref _dueDateError, value); }

    /// <summary>截止日期控件水印提示：显示当天日期作为输入模板，随每天日期更新。</summary>
    public string DueDatePlaceholder => DueDate.ToDisplayString(_now().Date);

    /// <summary>到期展示文案（如"已过期""今天到期"，未设置则为空串）。</summary>
    public string DueText
    {
        get => _dueText;
        private set
        {
            if (SetProperty(ref _dueText, value))
                OnPropertyChanged(nameof(HasDueText));
        }
    }

    /// <summary>是否有到期提示文案（驱动截止日期输入框的对齐方式：无提示居中、有提示左右分布）。</summary>
    public bool HasDueText => !string.IsNullOrEmpty(DueText);

    public bool IsOverdue { get => _isOverdue; private set => SetProperty(ref _isOverdue, value); }
    public bool IsDueToday { get => _isDueToday; private set => SetProperty(ref _isDueToday, value); }

    public ICommand BeginEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SetDueDateCommand { get; }
    public ICommand ClearDueDateCommand { get; }
    public ICommand PrevYearCommand { get; }
    public ICommand NextYearCommand { get; }
    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }

    private void BeginEdit()
    {
        if (_item.IsCompleted)
            return;
        _editText = _item.Title;
        EditError = "";
        IsEditing = true;
        OnPropertyChanged(nameof(IsNotEditing));
    }

    private void CommitEdit()
    {
        if (!_isEditing)
            return;

        var trimmed = _editText?.Trim() ?? "";
        var error = TaskTitle.Validate(trimmed);
        if (error is not null)
        {
            EditError = error;
            return;
        }

        if (string.Equals(trimmed, _item.Title, StringComparison.Ordinal))
        {
            // 内容未变：不写库，仅退出编辑态。
            IsEditing = false;
            OnPropertyChanged(nameof(IsNotEditing));
            return;
        }

        IsEditing = false;
        OnPropertyChanged(nameof(IsNotEditing));
        _repo.Rename(_item.Id, trimmed);
    }

    private void CancelEdit()
    {
        if (!_isEditing)
            return;
        _editText = _item.Title;
        EditError = "";
        IsEditing = false;
        OnPropertyChanged(nameof(IsNotEditing));
    }

    private void ApplyDueDate()
    {
        var text = DueDateText?.Trim() ?? "";

        if (text.Length == 0)
        {
            if (_item.DueDate.HasValue)
                _repo.SetDueDate(_item.Id, null);
            DueDateError = "";
            return;
        }

        var date = DueDate.TryParse(text);
        if (date.HasValue)
        {
            _repo.SetDueDate(_item.Id, date.Value);
            DueDateError = "";
        }
        else
        {
            DueDateError = $"日期格式无效，请输入 {DueDate.FormatHint}（示例：{DueDate.ToDisplayString(DateTime.Today)}）";
        }
    }

    /// <summary>
    /// 依据当前服务器时间刷新到期展示文案与高亮标记。
    /// 由 MainWindowViewModel 的提醒定时器周期性调用，以便"今天到期/已过期"随时间正确变化。
    /// </summary>
    public void RefreshDue()
    {
        OnPropertyChanged(nameof(DueDateSelectedDate));
        OnPropertyChanged(nameof(DueDateDisplayText));
        OnPropertyChanged(nameof(IsDueDateSet));
        var due = _item.DueDate;
        if (!due.HasValue)
        {
            DueText = "";
            IsOverdue = false;
            IsDueToday = false;
            if (!IsDueDateEditing)
                SetDueDateText("");
            OnPropertyChanged(nameof(DueDatePlaceholder));
            return;
        }

        var date = due.Value.Date;
        var today = _now().Date;

        if (!_item.IsCompleted && date < today)
        {
            IsOverdue = true;
            IsDueToday = false;
            DueText = "已过期";
        }
        else if (date == today)
        {
            IsOverdue = false;
            IsDueToday = true;
            DueText = "今天到期";
        }
        else
        {
            // 未来日期：输入框已展示完整日期，下方不再重复显示。
            IsOverdue = false;
            IsDueToday = false;
            DueText = "";
        }

        if (!IsDueDateEditing)
            SetDueDateText(DueDate.ToDisplayString(due.Value));
    }

    private void SetDueDateText(string value)
    {
        if (_dueDateText != value)
        {
            _dueDateText = value;
            OnPropertyChanged(nameof(DueDateText));
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TodoItem.Title):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(TodoItem.Priority):
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(PriorityLabel));
                OnPropertyChanged(nameof(PriorityRank));
                OnPropertyChanged(nameof(IsHigh));
                OnPropertyChanged(nameof(IsMedium));
                OnPropertyChanged(nameof(IsLow));
                OnPropertyChanged(nameof(IsNone));
                break;
            case nameof(TodoItem.IsCompleted):
                // 广播完成状态与整行透明度，保证视图实时刷新（完成置灰）。
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(RowOpacity));
                RefreshDue();
                break;
            case nameof(TodoItem.DueDate):
                OnPropertyChanged(nameof(DueDateSelectedDate));
                OnPropertyChanged(nameof(DueDateDisplayText));
                OnPropertyChanged(nameof(IsDueDateSet));
                RefreshDue();
                break;
            case nameof(TodoItem.FolderId):
                // 归属变化时刷新下拉选中。
                OnPropertyChanged(nameof(SelectedFolder));
                break;
        }
    }
}