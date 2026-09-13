namespace TodoList.Models;

/// <summary>
/// 文件夹模型（纯数据，无业务逻辑）。
/// 它是 SQLite 权威源中"folder"表的投影；所有变更一律经由 Repository 读写。
/// 文件夹用于收纳待办条目：条目通过 <see cref="TodoItem.FolderId"/> 单归属到某文件夹（null 表示未归类）。
/// </summary>
public class MyFolder
{
    /// <summary>唯一标识（由 Repository 生成）。</summary>
    public string Id { get; set; } = "";

    /// <summary>文件夹名称。</summary>
    public string Name { get; set; } = "";
}