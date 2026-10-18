namespace TodoList.Database;

/// <summary>
/// 整库备份能力（阶段七）：导出当前库为副本文件、从备份文件导入替换当前库。
/// 由 <see cref="DatabaseService"/> 实现；ViewModel 只消费本接口，不触碰文件细节。
/// </summary>
public interface IDatabaseBackup
{
    /// <summary>当前权威库文件路径（只读）。</summary>
    string DbFilePath { get; }

    /// <summary>把当前库文件复制为 destPath 处的副本。返回是否成功。</summary>
    bool ExportTo(string destPath);

    /// <summary>
    /// 用 sourcePath 的备份替换当前库：校验源文件 → 当前库自动改名保留 .bak → 复制替换。
    /// 运行中内存投影不热重载，导入成功后由调用方提示重启应用生效。返回是否成功。
    /// </summary>
    bool ImportFrom(string sourcePath);

    /// <summary>校验 sourcePath 是否为包含 tasks 表的 SQLite 库文件。</summary>
    bool IsValidBackupFile(string sourcePath);
}
