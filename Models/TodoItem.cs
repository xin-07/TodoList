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

    /// <summary>唯一标识（由 Repository 生成）。</summary>
    public string Id { get; set; } = "";

    /// <summary>任务标题。</summary>
    public string Title { get; set; } = "";

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

    /// <summary>所属文件夹 Id（null = 未归类）。单归属；仅允许 Repository 写入。</summary>
    public string? FolderId { get; set; }
}