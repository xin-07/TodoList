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

    public TodoRepository(DatabaseService db)
    {
        _db = db;
        // 启动时从权威源(SQLite)读取全量，构建投影。
        _items = new ObservableCollection<TodoItem>(_db.LoadAll().OrderBy(t => t.CreatedAt));
        _itemsView = new ReadOnlyObservableCollection<TodoItem>(_items);
    }

    public ReadOnlyObservableCollection<TodoItem> Items => _itemsView;

    public event Action? Changed;

    public void Add(string title)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            CreatedAt = DateTime.Now,
        };

        // 先写权威源，再同步投影。
        _db.Insert(item.Id, item.Title, item.CreatedAt);
        _items.Add(item);

        Changed?.Invoke();
    }

    public void SetCompleted(string id, bool isCompleted)
    {
        // 先写权威源。
        _db.UpdateCompleted(id, isCompleted);

        // 同步投影中的同一对象引用。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            item.IsCompleted = isCompleted;

        Changed?.Invoke();
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
}