using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using TodoList.Models;

namespace TodoList.Database;

/// <summary>
/// SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
/// 仅本类直接与 SQLite 打交道；
/// 上层 Repository 通过它读写，保证"单一权威源"落在数据库文件上。
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string dbPath)
    {
        // 确保数据库目录存在
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        Initialize();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = OpenConnection();

        // TodoList 当前只需要任务这一张表。is_completed 为 0/1。
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS tasks (
                    id          TEXT PRIMARY KEY,
                    title       TEXT NOT NULL,
                    is_completed INTEGER NOT NULL DEFAULT 0,
                    created_at  TEXT NOT NULL,
                    priority    TEXT NOT NULL DEFAULT 'None',
                    due_date    TEXT NULL,
                    folder_id   TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS folder (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // 迁移：对已存在的旧库，若缺少新增列则用 ALTER TABLE 补齐，保证老库升级不报错。
        EnsureColumn(conn, "priority", "TEXT NOT NULL DEFAULT 'None'");
        EnsureColumn(conn, "due_date", "TEXT NULL");
        EnsureColumn(conn, "folder_id", "TEXT NULL");

        // folder_id 查询索引：提升"按文件夹归类"类查询性能。置于补列之后，确保旧库先有该列再建索引。
        using (var idx = conn.CreateCommand())
        {
            idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_tasks_folder_id ON tasks(folder_id);";
            idx.ExecuteNonQuery();
        }
    }

    /// <summary>若指定列不存在则 ALTER TABLE 追加。幂等，可安全重复执行。</summary>
    private static void EnsureColumn(SqliteConnection conn, string column, string definition)
    {
        var exists = false;
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(tasks);";
            using var reader = probe.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE tasks ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    public IReadOnlyList<TodoItem> LoadAll()
    {
        var result = new List<TodoItem>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, is_completed, created_at, priority, due_date, folder_id FROM tasks;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var priority = TaskPriority.None;
            if (!reader.IsDBNull(4)
                && Enum.TryParse<TaskPriority>(reader.GetString(4), ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                priority = parsed;
            }

            result.Add(new TodoItem
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                IsCompleted = reader.GetInt64(2) != 0,
                CreatedAt = ParseDate(reader.GetString(3)) ?? default,
                Priority = priority,
                DueDate = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
                FolderId = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }
        return result;
    }

    /// <summary>
    /// 严格按 round-trip("o") 不变文化解析日期，与写入端的 <c>ToString("o")</c> 对称。
    /// 对非 "o" 的存量数据兜底走 <see cref="DateTime.Parse"/>（保留旧行为），避免读库抛错。
    /// </summary>
    private static DateTime? ParseDate(string raw)
    {
        if (DateTime.TryParseExact(raw, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exact))
            return exact;
        return DateTime.Parse(raw);
    }

    public void Insert(string id, string title, DateTime createdAt)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO tasks (id, title, is_completed, created_at, priority, due_date) VALUES ($id, $title, 0, $created_at, 'None', NULL);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$created_at", createdAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void UpdateCompleted(string id, bool isCompleted)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET is_completed = $is_completed WHERE id = $id;";
        cmd.Parameters.AddWithValue("$is_completed", isCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tasks WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>重命名标题。超出标题长度上限时按 MaxLength 截断，上限常量见 TaskTitle.MaxLength。</summary>
    public void UpdateTitle(string id, string title)
    {
        var normalized = (title ?? "").Trim();
        if (normalized.Length > TaskTitle.MaxLength)
            normalized = normalized[..TaskTitle.MaxLength];

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET title = $title WHERE id = $id;";
        cmd.Parameters.AddWithValue("$title", normalized);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdatePriority(string id, TaskPriority priority)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET priority = $priority WHERE id = $id;";
        cmd.Parameters.AddWithValue("$priority", priority.ToString());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateDueDate(string id, DateTime? date)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = date is null
            ? "UPDATE tasks SET due_date = NULL WHERE id = $id;"
            : "UPDATE tasks SET due_date = $due_date WHERE id = $id;";
        if (date is not null)
            cmd.Parameters.AddWithValue("$due_date", date.Value.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>读取全部文件夹（按 Id 排序，保持稳定顺序）。</summary>
    public IReadOnlyList<MyFolder> LoadFolders()
    {
        var result = new List<MyFolder>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM folder ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MyFolder
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
            });
        }
        return result;
    }

    public void InsertFolder(string id, string name)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO folder (id, name) VALUES ($id, $name);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    /// <summary>重命名文件夹。名称超出上限时按 MaxLength 截断，上限见 FolderName.MaxLength。</summary>
    public void RenameFolder(string id, string name)
    {
        var normalized = (name ?? "").Trim();
        if (normalized.Length > FolderName.MaxLength)
            normalized = normalized[..FolderName.MaxLength];

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE folder SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", normalized);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 删除文件夹，并连同其下所有条目一并删除。
    /// 返回被一并删除的条目数（供确认提示使用）。
    /// </summary>
    public int DeleteFolder(string id)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM tasks WHERE folder_id = $id;";
            count.Parameters.AddWithValue("$id", id);
            var deleted = Convert.ToInt32(count.ExecuteScalar());

            using var delTasks = conn.CreateCommand();
            delTasks.Transaction = tx;
            delTasks.CommandText = "DELETE FROM tasks WHERE folder_id = $id;";
            delTasks.Parameters.AddWithValue("$id", id);
            delTasks.ExecuteNonQuery();

            using var delFolder = conn.CreateCommand();
            delFolder.Transaction = tx;
            delFolder.CommandText = "DELETE FROM folder WHERE id = $id;";
            delFolder.Parameters.AddWithValue("$id", id);
            delFolder.ExecuteNonQuery();

            tx.Commit();
            return deleted;
        }
    }

    /// <summary>设置/清除条目归属文件夹。folderId 传 null 表示移到未归类。</summary>
    public void UpdateTaskFolder(string id, string? folderId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = folderId is null
            ? "UPDATE tasks SET folder_id = NULL WHERE id = $id;"
            : "UPDATE tasks SET folder_id = $folder_id WHERE id = $id;";
        if (folderId is not null)
            cmd.Parameters.AddWithValue("$folder_id", folderId);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}