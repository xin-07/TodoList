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
public class DatabaseService : IDatabaseBackup
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DatabaseService(string dbPath)
    {
        // 确保数据库目录存在
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        _dbPath = dbPath;
        Initialize();
    }

    /// <summary>当前权威库文件路径（只读，备份用）。</summary>
    public string DbFilePath => _dbPath;

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
                    folder_id   TEXT NULL,
                    recurrence  TEXT NOT NULL DEFAULT 'None',
                    recurrence_interval INTEGER NOT NULL DEFAULT 1,
                    recurrence_remaining INTEGER NULL,
                    completed_at TEXT NULL
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
        EnsureColumn(conn, "recurrence", "TEXT NOT NULL DEFAULT 'None'");
        EnsureColumn(conn, "recurrence_interval", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "recurrence_remaining", "INTEGER NULL");
        EnsureColumn(conn, "completed_at", "TEXT NULL");

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
        cmd.CommandText = "SELECT id, title, is_completed, created_at, priority, due_date, folder_id, recurrence, recurrence_interval, recurrence_remaining, completed_at FROM tasks;";
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

            var recurrence = RecurrenceType.None;
            if (!reader.IsDBNull(7)
                && Enum.TryParse<RecurrenceType>(reader.GetString(7), ignoreCase: true, out var parsedRecurrence)
                && Enum.IsDefined(parsedRecurrence))
            {
                recurrence = parsedRecurrence;
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
                Recurrence = recurrence,
                RecurrenceInterval = reader.GetInt64(8) >= 1 ? (int)reader.GetInt64(8) : 1,
                RecurrenceRemaining = reader.IsDBNull(9) ? null : (int)Math.Max(1, reader.GetInt64(9)),
                CompletedAt = reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)),
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

    /// <summary>新增任务。folderId 随插入一次写入，传 null 表示未归类。</summary>
    public void Insert(string id, string title, DateTime createdAt, string? folderId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO tasks (id, title, is_completed, created_at, priority, due_date, folder_id) VALUES ($id, $title, 0, $created_at, 'None', NULL, $folder_id);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$created_at", createdAt.ToString("o"));
        cmd.Parameters.AddWithValue("$folder_id", (object?)folderId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 写入完成状态。isCompleted=true 时 completedAt 为完成时间（由仓库注入时钟提供）；
    /// false 时取消完成并置 NULL。completedAt 与完成状态同一语句写入，保持一致。
    /// </summary>
    public void UpdateCompleted(string id, bool isCompleted, DateTime? completedAt)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET is_completed = $is_completed, completed_at = $completed_at WHERE id = $id;";
        cmd.Parameters.AddWithValue("$is_completed", isCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$completed_at", isCompleted && completedAt.HasValue
            ? completedAt.Value.ToString("o")
            : DBNull.Value);
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
        // 清除截止日期时联动清空周期规则（两者绑定，避免留下不可触发的悬空规则，见 SPEC 阶段三）。
        cmd.CommandText = date is null
            ? "UPDATE tasks SET due_date = NULL, recurrence = 'None', recurrence_interval = 1, recurrence_remaining = NULL WHERE id = $id;"
            : "UPDATE tasks SET due_date = $due_date WHERE id = $id;";
        if (date is not null)
            cmd.Parameters.AddWithValue("$due_date", date.Value.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>设置周期规则。type=None 时同时重置间隔为 1、剩余次数为 NULL（归为普通任务）。</summary>
    public void UpdateRecurrence(string id, RecurrenceType type, int interval, int? remaining)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tasks SET recurrence = $type, recurrence_interval = $interval, recurrence_remaining = $remaining WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.Parameters.AddWithValue("$interval", interval);
        cmd.Parameters.AddWithValue("$remaining", (object?)remaining ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 周期任务完成时的一次性滚动写入（单语句保证原子性）：
    /// 截止日期推进到下一期、剩余次数更新、完成状态归零（同条任务续期，不保留完成历史）。
    /// </summary>
    public void RollRecurrence(string id, DateTime nextDue, int? remaining)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET due_date = $due_date, recurrence_remaining = $remaining, is_completed = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$due_date", nextDue.ToString("o"));
        cmd.Parameters.AddWithValue("$remaining", (object?)remaining ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>把当前库文件复制为 destPath 处的副本（覆盖已存在的目标；先清连接池释放 Windows 文件句柄）。</summary>
    public bool ExportTo(string destPath)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            File.Copy(_dbPath, destPath, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// 用 sourcePath 的备份替换当前库：校验源 → 当前库改名保留 .bak → 复制替换；
    /// 复制失败时把 .bak 移回原位恢复。内存投影不热重载（调用方提示重启生效）。
    /// </summary>
    public bool ImportFrom(string sourcePath)
    {
        try
        {
            if (!IsValidBackupFile(sourcePath))
                return false;

            SqliteConnection.ClearAllPools();
            var bakPath = _dbPath + ".bak";
            if (File.Exists(_dbPath))
                File.Move(_dbPath, bakPath, overwrite: true);

            try
            {
                File.Copy(sourcePath, _dbPath, overwrite: true);
                return true;
            }
            catch (IOException)
            {
                // 复制失败：把留存的 .bak 移回原位，尽力恢复原状。
                if (File.Exists(bakPath) && !File.Exists(_dbPath))
                    File.Move(bakPath, _dbPath, overwrite: true);
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>校验 sourcePath 存在且含 tasks 表（防误选任意文件）。</summary>
    public bool IsValidBackupFile(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;

            var sourceConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();
            using var conn = new SqliteConnection(sourceConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'tasks';";
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // 非合法 SQLite 文件：视为无效备份。
            return false;
        }
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