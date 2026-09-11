using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    private bool _isEditing;
    private string _editText = "";
    private string _editError = "";
    private string _dueDateText = "";
    private string _dueDateError = "";
    private string _dueText = "";
    private bool _isOverdue;
    private bool _isDueToday;

    public TodoItemViewModel(ITodoRepository repo, TodoItem item)
    {
        _repo = repo;
        _item = item;
        _item.PropertyChanged += OnItemPropertyChanged;
        _editText = item.Title;
        _dueDateText = item.DueDate?.ToString("yyyy-MM-dd") ?? "";
        RefreshDue();

        BeginEditCommand = new RelayCommand(_ => BeginEdit());
        SaveEditCommand = new RelayCommand(_ => CommitEdit());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        SetDueDateCommand = new RelayCommand(_ => ApplyDueDate());
        SetPriorityCommand = new RelayCommand(p => SetPriority(p is TaskPriority tp ? tp : default));
    }

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

    /// <summary>到期展示文案（如"已过期""今天到期"，未设置则为空串）。</summary>
    public string DueText { get => _dueText; private set => SetProperty(ref _dueText, value); }

    public bool IsOverdue { get => _isOverdue; private set => SetProperty(ref _isOverdue, value); }
    public bool IsDueToday { get => _isDueToday; private set => SetProperty(ref _isDueToday, value); }

    public ICommand BeginEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SetDueDateCommand { get; }
    public ICommand SetPriorityCommand { get; }

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

        if (DateTime.TryParse(text, out var date))
        {
            _repo.SetDueDate(_item.Id, date);
            DueDateError = "";
        }
        else
        {
            DueDateError = "日期无效（示例：2026-09-11）";
        }
    }

    private void SetPriority(TaskPriority priority)
    {
        if (priority != _item.Priority)
            _repo.SetPriority(_item.Id, priority);
    }

    /// <summary>
    /// 依据当前服务器时间刷新到期展示文案与高亮标记。
    /// 由 MainWindowViewModel 的提醒定时器周期性调用，以便"今天到期/已过期"随时间正确变化。
    /// </summary>
    public void RefreshDue()
    {
        var due = _item.DueDate;
        if (!due.HasValue)
        {
            DueText = "";
            IsOverdue = false;
            IsDueToday = false;
            SetDueDateText("");
            return;
        }

        var date = due.Value.Date;
        var today = DateTime.Today;

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
            IsOverdue = false;
            IsDueToday = false;
            DueText = due.Value.ToString("MM-dd");
        }

        SetDueDateText(due.Value.ToString("yyyy-MM-dd"));
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
            case nameof(TodoItem.DueDate):
            case nameof(TodoItem.IsCompleted):
                RefreshDue();
                break;
        }
    }
}