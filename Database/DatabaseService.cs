using System;
using System.Collections.Generic;
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
        using var cmd = conn.CreateCommand();
        // TodoList 当前只需要任务这一张表。is_completed 为 0/1。
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
                id          TEXT PRIMARY KEY,
                title       TEXT NOT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                created_at  TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TodoItem> LoadAll()
    {
        var result = new List<TodoItem>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, is_completed, created_at FROM tasks;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TodoItem
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                IsCompleted = reader.GetInt64(2) != 0,
                CreatedAt = DateTime.Parse(reader.GetString(3)),
            });
        }
        return result;
    }

    public void Insert(string id, string title, DateTime createdAt)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO tasks (id, title, is_completed, created_at) VALUES ($id, $title, 0, $created_at);";
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
}