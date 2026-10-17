using System;
using TodoList.ViewModels;

namespace TodoList.Models;

/// <summary>
/// 任务数据模型（纯数据，无业务逻辑）。
/// 它只是 SQLite 权威源的"投影"：所有变更一律经由 Repository 读写。
/// </summary>
public class TodoItem : ViewModelBase
{
    private bool _isCompleted;
    private TaskPriority _priority;
    private DateTime? _dueDate;
    private RecurrenceType _recurrence;
    private int _recurrenceInterval = 1;
    private int? _recurrenceRemaining;
    private string _title = "";
    private string? _folderId;

    /// <summary>唯一标识（由 Repository 生成）。</summary>
    public string Id { get; set; } = "";

    /// <summary>任务标题。</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>是否已完成。仅允许 Repository 写入，界面是只读展示。</summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>任务优先级，默认 None。仅允许 Repository 写入。</summary>
    public TaskPriority Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    /// <summary>任务截止时间（可空）。仅允许 Repository 写入。</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    /// <summary>周期规则（None=不重复；规则必须建立在已设截止日期之上）。仅允许 Repository 写入。</summary>
    public RecurrenceType Recurrence
    {
        get => _recurrence;
        set => SetProperty(ref _recurrence, value);
    }

    /// <summary>周期间隔：每 N 个周期单位滚动一次（≥1）。仅允许 Repository 写入。</summary>
    public int RecurrenceInterval
    {
        get => _recurrenceInterval;
        set => SetProperty(ref _recurrenceInterval, value);
    }

    /// <summary>剩余发生次数（含当前待办这一次；null=无限）。仅允许 Repository 写入。</summary>
    public int? RecurrenceRemaining
    {
        get => _recurrenceRemaining;
        set => SetProperty(ref _recurrenceRemaining, value);
    }

    /// <summary>所属文件夹 Id（null = 未归类）。单归属；仅允许 Repository 写入。</summary>
    public string? FolderId
    {
        get => _folderId;
        set => SetProperty(ref _folderId, value);
    }
}