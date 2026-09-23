using System;
using System.Globalization;
using System.IO;
using System.Linq;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// TodoRepository 集成测试：全部使用临时独立的 SQLite 库（不触碰 data/todo.db），
/// 并通过 repo.Items（投影）与 DatabaseService.LoadAll（直接查权威源）双路径交叉断言 SSOT 一致。
/// </summary>
public class TodoRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public TodoRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "todolist_tests_" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // 临时文件清理失败不影响断言；文件由系统临时目录回收。
            }
        }
    }

    private TodoRepository NewRepository() => new(new DatabaseService(_dbPath));

    [Fact]
    public void Add_落库且投影出现_两条路径一致()
    {
        var repo = NewRepository();
        var ok = repo.Add("  买牛奶  ");
        Assert.True(ok);

        var item = Assert.Single(repo.Items);
        // Trim 后入库。
        Assert.Equal("买牛奶", item.Title);
        Assert.Equal(TaskPriority.None, item.Priority);
        Assert.Null(item.DueDate);

        // 直接查权威源交叉验证。
        var fromDb = new DatabaseService(_dbPath).LoadAll();
        var dbItem = Assert.Single(fromDb);
        Assert.Equal("买牛奶", dbItem.Title);
        Assert.Equal(TaskPriority.None, dbItem.Priority);
        Assert.Null(dbItem.DueDate);
    }

    [Fact]
    public void Add_空白或超长标题被拒绝_不入库()
    {
        var repo = NewRepository();

        Assert.False(repo.Add("   "));
        Assert.False(repo.Add(""));

        var tooLong = new string('a', TaskTitle.MaxLength + 1);
        Assert.False(repo.Add(tooLong));

        Assert.Empty(repo.Items);
        Assert.Empty(new DatabaseService(_dbPath).LoadAll());
    }

    [Fact]
    public void Rename_改库且改投影()
    {
        var repo = NewRepository();
        repo.Add("旧标题");
        var id = repo.Items.Single().Id;

        Assert.True(repo.Rename(id, "  新标题  "));
        Assert.Equal("新标题", repo.Items.Single().Title);
        Assert.Equal("新标题", new DatabaseService(_dbPath).LoadAll().Single().Title);
    }

    [Fact]
    public void Rename_不存在的id_返回false()
    {
        var repo = NewRepository();
        Assert.False(repo.Rename("no-such-id", "任意标题"));
    }

    [Fact]
    public void SetPriority_落库且同步投影()
    {
        var repo = NewRepository();
        repo.Add("任务");
        var id = repo.Items.Single().Id;

        repo.SetPriority(id, TaskPriority.High);

        Assert.Equal(TaskPriority.High, repo.Items.Single().Priority);
        Assert.Equal(TaskPriority.High, new DatabaseService(_dbPath).LoadAll().Single().Priority);
    }

    [Fact]
    public void SetDueDate_设置与清除_均落库()
    {
        var repo = NewRepository();
        repo.Add("任务");
        var id = repo.Items.Single().Id;
        var due = new DateTime(2026, 12, 31, 18, 30, 0, DateTimeKind.Local);

        repo.SetDueDate(id, due);
        Assert.Equal(due, repo.Items.Single().DueDate);
        Assert.Equal(due, new DatabaseService(_dbPath).LoadAll().Single().DueDate);

        // 清除。
        repo.SetDueDate(id, null);
        Assert.Null(repo.Items.Single().DueDate);
        Assert.Null(new DatabaseService(_dbPath).LoadAll().Single().DueDate);
    }

    [Fact]
    public void Delete_从库与投影同时移除()
    {
        var repo = NewRepository();
        repo.Add("A");
        repo.Add("B");
        var idA = repo.Items.First(t => t.Title == "A").Id;

        repo.Remove(idA);

        Assert.Single(repo.Items);
        Assert.Equal("B", repo.Items.Single().Title);
        Assert.Single(new DatabaseService(_dbPath).LoadAll());
    }

    [Fact]
    public void 持久化_关闭重开加载全部字段()
    {
        var due = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Local);

        var first = NewRepository();
        first.Add("买牛奶");
        var idA = first.Items.Single().Id;
        first.SetPriority(idA, TaskPriority.Medium);
        first.SetDueDate(idA, due);

        // 重新打开仓库，验证从权威源加载回全部变更。
        var reopened = NewRepository();
        var item = Assert.Single(reopened.Items);
        Assert.Equal("买牛奶", item.Title);
        Assert.Equal(TaskPriority.Medium, item.Priority);
        Assert.Equal(due, item.DueDate);
    }

    [Fact]
    public void 读库_在非固定文化下_日期按o格式正确解析()
    {
        // 回归 B4-T10：读库改用 InvariantCulture + round-trip("o") 解析，不依赖进程 CurrentCulture。
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("de-DE");
        try
        {
            var first = NewRepository();
            first.Add("任务");
            var idA = first.Items.Single().Id;
            var due = new DateTime(2026, 12, 31, 18, 30, 0, DateTimeKind.Local);
            first.SetDueDate(idA, due);

            var reopened = NewRepository();
            Assert.Equal(due, reopened.Items.Single().DueDate);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 旧库迁移_新增列不存在时可升级不报错()
    {
        // 手工创建一个只有旧列（无 priority/due_date）的库。
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE tasks (
                    id          TEXT PRIMARY KEY,
                    title       TEXT NOT NULL,
                    is_completed INTEGER NOT NULL DEFAULT 0,
                    created_at  TEXT NOT NULL
                );
                INSERT INTO tasks (id, title, is_completed, created_at)
                VALUES ('legacy-1', '旧任务', 0, '2026-01-01T00:00:00');
                """;
            cmd.ExecuteNonQuery();
        }

        // 打开现有库（触发迁移补齐新列），读取不报错且默认值正确。
        var migrated = NewRepository();
        var item = Assert.Single(migrated.Items);
        Assert.Equal("旧任务", item.Title);
        Assert.Equal(TaskPriority.None, item.Priority);
        Assert.Null(item.DueDate);

        // 再开一次，确认迁移幂等。
        var idempotent = NewRepository();
        Assert.Single(idempotent.Items);
    }

    [Fact]
    public void AddFolder_落库且投影出现_默认未归类()
    {
        var repo = NewRepository();
        Assert.True(repo.AddFolder("  工作  "));

        var folder = Assert.Single(repo.Folders);
        Assert.Equal("工作", folder.Name);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public void AddFolder_空白或超长名称被拒绝()
    {
        var repo = NewRepository();
        Assert.False(repo.AddFolder("   "));
        Assert.False(repo.AddFolder(""));

        var tooLong = new string('a', FolderName.MaxLength + 1);
        Assert.False(repo.AddFolder(tooLong));

        Assert.Empty(repo.Folders);
    }

    [Fact]
    public void RenameFolder_改库且改投影()
    {
        var repo = NewRepository();
        repo.AddFolder("旧名");
        var id = repo.Folders.Single().Id;

        Assert.True(repo.RenameFolder(id, "  新名  "));
        Assert.Equal("新名", repo.Folders.Single().Name);
    }

    [Fact]
    public void RenameFolder_不存在id_返回false()
    {
        var repo = NewRepository();
        Assert.False(repo.RenameFolder("no-such-id", "任意名"));
    }

    [Fact]
    public void SetFolder_归属与清除_均落库且同步投影()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;
        repo.Add("任务");
        var taskId = repo.Items.Single().Id;

        // 归属到文件夹。
        repo.SetFolder(taskId, folderId);
        Assert.Equal(folderId, repo.Items.Single().FolderId);
        Assert.Equal(folderId, new DatabaseService(_dbPath).LoadAll().Single().FolderId);

        // 清除（回未归类）。
        repo.SetFolder(taskId, null);
        Assert.Null(repo.Items.Single().FolderId);
        Assert.Null(new DatabaseService(_dbPath).LoadAll().Single().FolderId);
    }

    [Fact]
    public void Add_带归属_随插入一次落库_重开保留()
    {
        var first = NewRepository();
        first.AddFolder("工作");
        var folderId = first.Folders.Single().Id;

        Assert.True(first.Add("买菜", folderId));

        // 投影与权威源一致（一次 INSERT 即带归属）。
        Assert.Equal(folderId, first.Items.Single().FolderId);
        Assert.Equal(folderId, new DatabaseService(_dbPath).LoadAll().Single().FolderId);

        // 重开仓库，归属从权威源加载回。
        var reopened = NewRepository();
        Assert.Equal(folderId, reopened.Items.Single().FolderId);
    }

    [Fact]
    public void Add_不传归属_默认未归类()
    {
        var repo = NewRepository();

        Assert.True(repo.Add("买菜"));

        Assert.Null(repo.Items.Single().FolderId);
        Assert.Null(new DatabaseService(_dbPath).LoadAll().Single().FolderId);
    }

    [Fact]
    public void DeleteFolder_连同内条目一并删除_并返回条目数()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        var folderId = repo.Folders.Single().Id;
        repo.Add("A");
        repo.Add("B");
        foreach (var t in repo.Items.ToList())
            repo.SetFolder(t.Id, folderId);

        var deleted = repo.DeleteFolder(folderId);

        Assert.Equal(2, deleted);
        Assert.Empty(repo.Folders);
        Assert.Empty(repo.Items);
        Assert.Empty(new DatabaseService(_dbPath).LoadAll());
    }

    [Fact]
    public void DeleteFolder_不影响其他文件夹与未归类条目()
    {
        var repo = NewRepository();
        repo.AddFolder("工作");
        repo.AddFolder("生活");
        var workId = repo.Folders.First(f => f.Name == "工作").Id;
        repo.Add("工作任务");
        repo.Add("生活任务");
        repo.Add("未归类任务");
        repo.SetFolder(repo.Items.First(t => t.Title == "工作任务").Id, workId);

        var deleted = repo.DeleteFolder(workId);

        Assert.Equal(1, deleted);
        Assert.Single(repo.Folders);
        Assert.Equal("生活", repo.Folders.Single().Name);
        Assert.Equal(2, repo.Items.Count); // 生活任务 + 未归类任务
    }
}