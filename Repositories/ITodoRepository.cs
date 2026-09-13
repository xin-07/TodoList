using System;
using TodoList.Models;

namespace TodoList.Repositories;

/// <summary>
/// 任务仓库接口：定义"任务数据"的唯一边界。
/// 所有任务的增删改都必须经过实现本接口的单一权威源，
/// 任何本接口之外的地方都不允许直接修改任务数据。
/// </summary>
public interface ITodoRepository
{
    /// <summary>当前任务列表的只读投影。只读，不允许外部 Add/Remove。</summary>
    System.Collections.ObjectModel.ReadOnlyObservableCollection<TodoItem> Items { get; }

    /// <summary>当前文件夹列表的只读投影。只读，不允许外部 Add/Remove。</summary>
    System.Collections.ObjectModel.ReadOnlyObservableCollection<MyFolder> Folders { get; }

    /// <summary>增删改发生后触发，供 ViewModel 刷新统计等派生数据。</summary>
    event Action? Changed;

    /// <summary>新增一条任务。返回是否成功（空白/超长标题会被拒绝）。</summary>
    bool Add(string title);

    /// <summary>重命名一条任务的标题。返回是否成功（标题不存在或非法时返回 false）。</summary>
    bool Rename(string id, string title);

    /// <summary>设置一条任务的优先级。</summary>
    void SetPriority(string id, TaskPriority priority);

    /// <summary>设置一条任务的截止时间；传 null 表示清除。</summary>
    void SetDueDate(string id, DateTime? date);

    /// <summary>切换任务的完成状态。true=已完成，false=未完成。</summary>
    void SetCompleted(string id, bool isCompleted);

    /// <summary>删除指定任务。</summary>
    void Remove(string id);

    /// <summary>新增一个文件夹。返回是否成功（名称非法时返回 false）。</summary>
    bool AddFolder(string name);

    /// <summary>重命名一个文件夹。返回是否成功（名称非法或不存在时返回 false）。</summary>
    bool RenameFolder(string id, string name);

    /// <summary>删除一个文件夹并连同其下所有条目一并删除。返回被一并删除的条目数（不存在时返回 0）。</summary>
    int DeleteFolder(string id);

    /// <summary>设置/清除任务归属文件夹；folderId 传 null 表示移到未归类。</summary>
    void SetFolder(string id, string? folderId);
}