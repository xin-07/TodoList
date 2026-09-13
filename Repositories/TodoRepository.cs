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

    public bool Add(string title)
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
        };

        // 先写权威源，再同步投影。
        _db.Insert(item.Id, item.Title, item.CreatedAt);
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
        // 先写权威源。
        _db.UpdateDueDate(id, date);

        // 同步投影中的同一对象引用。
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            item.DueDate = date;

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