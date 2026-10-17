using System;
using System.Collections.ObjectModel;
using System.Linq;
using TodoList.Database;
using TodoList.Models;

namespace TodoList.Repositories;

/// <summary>
/// 任务仓库实现：应用内唯一权威数据源。
///
/// 权威真相存储在 SQLite（DatabaseService）。本类在内存中维护一份
/// ObservableCollection 投影用于界面实时更新，但它是 SQLite 的同步镜像，
/// 并非第二份真相。任何变更都"先落库、再更新投影"，保证二者永远一致。
///
/// 外部拿到的是只读投影，从机制上阻止 ViewModel 私自改数据。
/// </summary>
public class TodoRepository : ITodoRepository
{
    private readonly DatabaseService _db;
    private readonly ObservableCollection<TodoItem> _items;
    private readonly ReadOnlyObservableCollection<TodoItem> _itemsView;
    private readonly ObservableCollection<MyFolder> _folders;
    private readonly ReadOnlyObservableCollection<MyFolder> _foldersView;

    public TodoRepository(DatabaseService db)
    {
        _db = db;
        // 启动时从权威源(SQLite)读取全量，构建投影。
        _items = new ObservableCollection<TodoItem>(_db.LoadAll().OrderBy(t => t.CreatedAt));
        _itemsView = new ReadOnlyObservableCollection<TodoItem>(_items);
        _folders = new ObservableCollection<MyFolder>(_db.LoadFolders());
        _foldersView = new ReadOnlyObservableCollection<MyFolder>(_folders);
    }

    public ReadOnlyObservableCollection<TodoItem> Items => _itemsView;

    public ReadOnlyObservableCollection<MyFolder> Folders => _foldersView;

    public event Action? Changed;

    public bool Add(string title, string? folderId = null)
    {
        var error = TaskTitle.Validate(title);
        if (error is not null)
            return false;

        var normalized = title.Trim();
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = normalized,
            CreatedAt = DateTime.Now,
            FolderId = folderId,
        };

        // 先写权威源（归属随插入一次落库），再同步投影。
        _db.Insert(item.Id, item.Title, item.CreatedAt, folderId);
        _items.Add(item);

        Changed?.Invoke();
        return true;
    }

    public bool Rename(string id, string title)
    {
        var error = TaskTitle.Validate(title);
        if (error is not null)
            return false;

        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is null)
            return false;

        // 先写权威源（DatabaseService 内部按 MaxLength 截断），再同步投影。
        var normalized = title.Trim();
        _db.UpdateTitle(id, normalized);
        item.Title = normalized;

        Changed?.Invoke();
        return true;
    }

    public void SetPriority(string id, TaskPriority priority)
    {
        // 先写权威源。
        _db.UpdatePriority(id, priority);

        // 同步投影中的同一对象引用。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            item.Priority = priority;

        Changed?.Invoke();
    }

    public void SetDueDate(string id, DateTime? date)
    {
        // 先写权威源（清空截止日期时，DatabaseService 会联动清空周期规则列）。
        _db.UpdateDueDate(id, date);

        // 同步投影中的同一对象引用（清截止日期时一并清空投影中的周期规则）。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
        {
            item.DueDate = date;
            if (date is null)
            {
                item.Recurrence = RecurrenceType.None;
                item.RecurrenceInterval = 1;
                item.RecurrenceRemaining = null;
            }
        }

        Changed?.Invoke();
    }

    public bool SetRecurrence(string id, RecurrenceType type, int interval, int? remaining)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is null)
            return false;

        // 周期规则必须建立在截止日期之上，且间隔/剩余次数（非无限时）为 ≥1 的有效值。
        if (type != RecurrenceType.None)
        {
            if (!item.DueDate.HasValue || interval < 1 || remaining is < 1)
                return false;
        }

        // 先写权威源（None 归一化为 1/NULL），再同步投影。
        _db.UpdateRecurrence(id, type, type == RecurrenceType.None ? 1 : interval, type == RecurrenceType.None ? null : remaining);
        item.Recurrence = type;
        item.RecurrenceInterval = type == RecurrenceType.None ? 1 : interval;
        item.RecurrenceRemaining = type == RecurrenceType.None ? null : remaining;

        Changed?.Invoke();
        return true;
    }

    public void SetCompleted(string id, bool isCompleted)
    {
        // [DEFERRED 2026-10-18 阶段三周期性任务暂缓（用户决定）] 滚动分支注释停用；
        // 重新启用：解开下段注释，并同步解开 MainWindow.axaml 内"重复"UI 注释块；
        // 悬空的周期规则（库中已设）在停用期间不会触发，完成一律按普通任务定格。
        // 4 个滚动行为测试带 Skip 挂起（RecurrenceTests.cs），随本分支一起恢复。
        /*
        if (isCompleted && TryRollRecurrence(id, out var rolled))
        {
            Changed?.Invoke();
            return;
        }
        */

        // 先写权威源。
        _db.UpdateCompleted(id, isCompleted);

        // 同步投影中的同一对象引用。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            item.IsCompleted = isCompleted;

        Changed?.Invoke();
    }

    /// <summary>
    /// 尝试按周期规则滚动一条正被完成的任务。返回是否发生滚动（rolled=投影内对应的任务对象）：
    ///  - 无周期规则或未设截止日期：不滚动（按普通任务完成）；
    ///  - 剩余次数取尽（=1，含当前这次）：完成定格，周期清除，归为普通任务；
    ///  - 其余：截止日期推进 N 个周期单位、剩余次数减一、完成状态归零。
    /// 权威源一次性写入，投影随后同步（属性变更沿既有事件链路回环收敛）。
    /// 调用点（SetCompleted 滚动分支）已随功能注释停用 [DEFERRED 2026-10-18]。
    /// </summary>
    private bool TryRollRecurrence(string id, out TodoItem? rolled)
    {
        rolled = null;
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is null || item.Recurrence == RecurrenceType.None || !item.DueDate.HasValue)
            return false;

        // 次数取尽：不再滚动，完成定格并清除周期规则。
        if (item.RecurrenceRemaining == 1)
        {
            _db.UpdateRecurrence(item.Id, RecurrenceType.None, 1, null);
            _db.UpdateCompleted(item.Id, true);

            item.Recurrence = RecurrenceType.None;
            item.RecurrenceInterval = 1;
            item.RecurrenceRemaining = null;
            item.IsCompleted = true;
            rolled = item;
            return true;
        }

        // 无限或尚有剩余：单步滚动到下一期（不追赶过去时期）。
        var nextDue = Recurrence.NextDue(item.DueDate.Value, item.Recurrence, item.RecurrenceInterval);
        var nextRemaining = item.RecurrenceRemaining - 1; // null 减一仍为 null（无限）

        _db.RollRecurrence(item.Id, nextDue, nextRemaining);

        // 先归零完成态再更新其余字段，让后续派生刷新看到最终状态（完成 → 复为待办）。
        item.IsCompleted = false;
        item.RecurrenceRemaining = nextRemaining;
        item.DueDate = nextDue;
        rolled = item;
        return true;
    }

    public void Remove(string id)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is null)
            return;

        // 先写权威源。
        _db.Delete(id);
        _items.Remove(item);

        Changed?.Invoke();
    }

    public bool AddFolder(string name)
    {
        var error = FolderName.Validate(name);
        if (error is not null)
            return false;

        var folder = new MyFolder
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
        };

        // 先写权威源，再同步投影。
        _db.InsertFolder(folder.Id, folder.Name);
        _folders.Add(folder);

        Changed?.Invoke();
        return true;
    }

    public bool RenameFolder(string id, string name)
    {
        var error = FolderName.Validate(name);
        if (error is not null)
            return false;

        var folder = _folders.FirstOrDefault(f => f.Id == id);
        if (folder is null)
            return false;

        // 先写权威源（DatabaseService 内部按 MaxLength 截断），再同步投影。
        _db.RenameFolder(id, name.Trim());
        folder.Name = name.Trim();

        Changed?.Invoke();
        return true;
    }

    public int DeleteFolder(string id)
    {
        // 先写权威源（连同其下条目一并删除），返回被删条目数。
        var deletedCount = _db.DeleteFolder(id);

        // 同步投影：先移除文件夹内条目，再移除文件夹本身。
        var removed = _items.Where(t => t.FolderId == id).ToList();
        foreach (var item in removed)
            _items.Remove(item);
        var folder = _folders.FirstOrDefault(f => f.Id == id);
        if (folder is not null)
            _folders.Remove(folder);

        Changed?.Invoke();
        return deletedCount;
    }

    public void SetFolder(string id, string? folderId)
    {
        // 先写权威源。
        _db.UpdateTaskFolder(id, folderId);

        // 同步投影中的同一对象引用。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            item.FolderId = folderId;

        Changed?.Invoke();
    }
}