using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TodoList.Database;
using TodoList.Models;
using TodoList.Repositories;
using TodoList.ViewModels;
using Xunit;

namespace TodoList.Tests;

/// <summary>
/// 阶段七（统计与备份）集成测试：临时独立 SQLite 库，交叉断言 completed_at 契约、
/// 全库口径统计（今日/本周/本月/完成率/近 14 天分桶）与整库导出/导入行为。
/// </summary>
public class StatsAndBackupTests : IDisposable
{
    /// <summary>统计基准时钟（从 2026-10-18 周日 15:00 起可偏移，决定完成时间落点）。</summary>
    private static readonly DateTime BaseNow = new(2026, 10, 18, 15, 0, 0);

    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            var candidates = new[] { path, path + ".bak" };
            foreach (var file in candidates.Where(File.Exists))
            {
                try { File.Delete(file); }
                catch (IOException) { /* 临时文件清理失败不影响断言 */ }
            }
        }
    }

    private string NewDbPath([System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"todolist_stats_{(caller ?? "t")}_{Guid.NewGuid().ToString("N")[..8]}.db");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>偏移天数可变的时钟：完成时间差N天的用法是完成前临时改 offset。</summary>
    private sealed class MutableClock
    {
        public int OffsetDays;
        public DateTime Now => BaseNow.AddDays(OffsetDays);
    }

    /// <summary>IDatabaseBackup 假实现：导出/导入成功与否可预设，路径直接记录。</summary>
    private sealed class FakeBackup : IDatabaseBackup
    {
        public string DbFilePath { get; } = @"C:\fake\todo.db";
        public bool ExportResult { get; init; } = true;
        public bool ImportResult { get; init; } = true;
        public string? LastExportPath;
        public string? LastImportPath;

        public bool ExportTo(string destPath) { LastExportPath = destPath; return ExportResult; }
        public bool ImportFrom(string sourcePath) { LastImportPath = sourcePath; return ImportResult; }
        public bool IsValidBackupFile(string sourcePath) => true;
    }

    [Fact]
    public void SetCompleted_完成写当前时间_取消完成置回NULL_双路径一致()
    {
        var clock = new MutableClock();
        var path = NewDbPath();
        var db = new DatabaseService(path);
        var repo = new TodoRepository(db, () => clock.Now);

        repo.Add("统计任务A");
        var id = repo.Items[0].Id;
        repo.SetCompleted(id, true);
        Assert.Equal(clock.Now, repo.Items[0].CompletedAt);

        var fromDb = new DatabaseService(path).LoadAll();
        Assert.Equal(clock.Now, Assert.Single(fromDb).CompletedAt);

        // 取消完成：completed_at 置 NULL。
        repo.SetCompleted(id, false);
        Assert.Null(repo.Items[0].CompletedAt);
        Assert.Null(new DatabaseService(path).LoadAll().Single().CompletedAt);
    }

    [Fact]
    public void 旧库NULL完成时间_加载不崩且不计入今日完成()
    {
        var path = NewDbPath();
        // 模拟旧库直接落一行已完成任务：无 completed_at（NULL），无 recurrence 列（迁移默认值）。
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    is_completed INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                );
                """;
            create.ExecuteNonQuery();
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO tasks (id, title, is_completed, created_at) VALUES ($id, $title, 1, $now);";
            insert.Parameters.AddWithValue("$id", "legacy-1");
            insert.Parameters.AddWithValue("$title", "旧库已完成任务");
            insert.Parameters.AddWithValue("$now", BaseNow.ToString("o"));
            insert.ExecuteNonQuery();
        }

        var repo = new TodoRepository(new DatabaseService(path), () => BaseNow);
        var vm = new MainWindowViewModel(repo, nowProvider: () => BaseNow);
        Assert.True(vm.Items.Single().IsCompleted);
        Assert.Null(vm.Items.Single().CompletedAt);
        Assert.Equal(1, vm.CompletedCount);
        // NULL 完成时间不参与按日期口径（不伪造历史）。
        Assert.Equal(0, vm.CompletedToday);
        Assert.Equal(0, vm.CompletedThisWeek);
        Assert.Equal(0, vm.CompletedThisMonth);
        // 完成率照常计入。
        Assert.Equal(100, vm.CompletionRate);
    }

    [Fact]
    public void 统计口径_今日本周本月完成率与近14天分桶()
    {
        var clock = new MutableClock();
        var path = NewDbPath();
        var repo = new TodoRepository(new DatabaseService(path), () => clock.Now);

        repo.Add("本日"); var tDaily = repo.Items[0].Id;
        repo.Add("本周"); var tWeek = repo.Items[1].Id;
        repo.Add("本月"); var tMonth = repo.Items[2].Id;
        repo.Add("太久"); var tOld = repo.Items[3].Id;
        repo.Add("未完"); var tOpen = repo.Items[4].Id;

        // 各完成时间：今天 / 本周周一(10-12)后 / 本月(10-01)后 / 本月前(9-28)。
        var completions = new Dictionary<string, int> { [tDaily] = 0, [tWeek] = -2, [tMonth] = -9, [tOld] = -20 };
        foreach (var (id, offset) in completions)
        {
            clock.OffsetDays = offset;
            repo.SetCompleted(id, true);
        }
        clock.OffsetDays = 0;

        // VM 用同一基准时钟实例化（构造一次 + repo.Changed 增量刷新，覆盖既有数据的统计与投影）。
        var vm = new MainWindowViewModel(repo, nowProvider: () => clock.Now);
        vm.RefreshStats();

        Assert.Equal(4, vm.CompletedCount);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(1, vm.CompletedToday);    // 今天完成 1。
        Assert.Equal(2, vm.CompletedThisWeek); // 今天 + 周五(10-16)。
        Assert.Equal(3, vm.CompletedThisMonth);// 加 10-09。
        Assert.Equal(80, vm.CompletionRate);   // 4 / 5。

        // 近 14 天窗口：F-13..F 共 14 天；今天与 F-2(索引 11)、F-9(索引 4) 各 1 条，其余为空。
        var bars = vm.RecentCompletionBars;
        Assert.Equal(14, bars.Count);
        Assert.Equal(BaseNow.ToString("M/d"), bars[13].Label);
        Assert.All(new[] { bars[13], bars[11], bars[4] }, b => Assert.Equal("1", b.CountText));
        Assert.Equal(120, bars[13].BarWidth);
        Assert.Equal(11, bars.Count(b => b.CountText == ""));
    }

    [Fact]
    public void 完成后统计经Changed增量刷新()
    {
        var path = NewDbPath();
        var repo = new TodoRepository(new DatabaseService(path), () => BaseNow);
        var vm = new MainWindowViewModel(repo, nowProvider: () => BaseNow);

        repo.Add("新完成");
        repo.SetCompleted(repo.Items[0].Id, true);

        Assert.Equal(1, vm.CompletedToday);
        Assert.Equal(100, vm.CompletionRate);
    }

    [Fact]
    public void ExportTo_副本落盘且内容可被新库读出()
    {
        var path = NewDbPath();
        var repo = new TodoRepository(new DatabaseService(path), () => BaseNow);
        repo.Add("备份对象");
        var db = new DatabaseService(path);

        var backupPath = NewDbPath();
        Assert.True(db.ExportTo(backupPath));
        Assert.True(File.Exists(backupPath));

        var fromBackup = new DatabaseService(backupPath).LoadAll();
        Assert.Equal("备份对象", Assert.Single(fromBackup).Title);
    }

    [Fact]
    public void ImportFrom_替换当前库生效当前库留bak()
    {
        var currentPath = NewDbPath();
        var otherPath = NewDbPath();

        // 当前库：旧数据。
        var currentRepo = new TodoRepository(new DatabaseService(currentPath), () => BaseNow);
        currentRepo.Add("旧数据");
        // 备份库：替换后的新数据。
        var backupDb = new DatabaseService(otherPath);
        new TodoRepository(backupDb).Add("导入数据");

        var currentDb = new DatabaseService(currentPath);
        Assert.True(currentDb.ImportFrom(otherPath));
        Assert.True(File.Exists(currentPath + ".bak"));

        // 当前库已被替换为新权威内容。
        var after = new DatabaseService(currentPath).LoadAll();
        Assert.Equal("导入数据", Assert.Single(after).Title);
        // .bak 内容是替换前的旧数据。
        var bak = new DatabaseService(currentPath + ".bak").LoadAll();
        Assert.Equal("旧数据", Assert.Single(bak).Title);

        // 内存投影不热重载（契约同生产提示）：既有 repo 实例的 Items 仍是启动时读入的旧数据，
        // 重启应用后才以新权威源载入——file 级已在上方 LoadAll 双路径验证。
    }

    [Fact]
    public void ImportFrom_非法源被拒_当前库不变()
    {
        var currentPath = NewDbPath();
        var currentRepo = new TodoRepository(new DatabaseService(currentPath), () => BaseNow);
        currentRepo.Add("原始数据");

        var notSqlite = NewDbPath();
        File.WriteAllText(notSqlite, "这不是一个 SQLite 文件");

        var db = new DatabaseService(currentPath);
        Assert.False(db.ImportFrom(notSqlite));
        Assert.Equal("原始数据", Assert.Single(db.LoadAll()).Title);
        Assert.False(File.Exists(currentPath + ".bak"));
    }

    [Fact]
    public void VM备份消息_成功_失败_取消与不可用()
    {
        var repo = new TodoRepository(new DatabaseService(NewDbPath()), () => BaseNow);
        var fake = new FakeBackup();
        var vm = new MainWindowViewModel(repo, backup: fake);

        vm.ExportBackup(null);
        Assert.Equal("", vm.BackupMessage);
        vm.ImportBackup(null);
        Assert.Equal("", vm.BackupMessage);

        vm.ExportBackup(@"C:\out\backup.db");
        Assert.Equal(@"C:\out\backup.db", fake.LastExportPath);
        Assert.Equal("导出完成", vm.BackupMessage);

        var failVm = new MainWindowViewModel(repo, backup: new FakeBackup { ExportResult = false });
        failVm.ExportBackup(@"C:\out\no.db");
        Assert.Equal("导出失败（目标路径可能无权限或被占用）", failVm.BackupMessage);

        vm.ImportBackup(@"C:\in\backup.db");
        Assert.Equal("已导入，重启 TodoList 后生效", vm.BackupMessage);

        var failImport = new MainWindowViewModel(repo, backup: new FakeBackup { ImportResult = false });
        failImport.ImportBackup(@"C:\in\backup.db");
        Assert.Equal("导入失败：所选文件不是有效的 TodoList 备份", failImport.BackupMessage);

        var 无备份 = new MainWindowViewModel(repo);
        无备份.ExportBackup(@"C:\out\any.db");
        Assert.Equal("备份功能不可用", 无备份.BackupMessage);
    }

    [Fact]
    public void VM备份命令_仅向View发请求事件不直接执行()
    {
        var repo = new TodoRepository(new DatabaseService(NewDbPath()), () => BaseNow);
        var vm = new MainWindowViewModel(repo, backup: new FakeBackup());

        var exported = 0;
        var imported = 0;
        vm.ExportBackupRequested += (_, _) => exported++;
        vm.ImportBackupRequested += (_, _) => imported++;

        vm.ExportBackupCommand.Execute(null);
        vm.ImportBackupCommand.Execute(null);

        Assert.Equal(1, exported);
        Assert.Equal(1, imported);
    }
}
