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

    /// <summary>增删改发生后触发，供 ViewModel 刷新统计等派生数据。</summary>
    event Action? Changed;

    /// <summary>新增一条任务。</summary>
    void Add(string title);

    /// <summary>切换任务的完成状态。true=已完成，false=未完成。</summary>
    void SetCompleted(string id, bool isCompleted);

    /// <summary>删除指定任务。</summary>
    void Remove(string id);
}