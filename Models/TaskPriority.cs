namespace TodoList.Models;

/// <summary>
/// 任务优先级。低→高代表紧急/重要程度的递增。
/// 该枚举是优先级定义在应用内的"单一权威源"；持久化时以其名称(None/Low/Medium/High)存入 SQLite。
/// </summary>
public enum TaskPriority
{
    /// <summary>无优先级（默认）。</summary>
    None,

    /// <summary>低。</summary>
    Low,

    /// <summary>中。</summary>
    Medium,

    /// <summary>高。</summary>
    High,
}