namespace TodoList.Models;

/// <summary>
/// 文件夹名称的"单一权威源"校验助手：Trim + 非空 + MaxLength。
/// 与 <see cref="TaskTitle"/> 模式一致，但文件夹名可独立调整上限。
/// 供 Repository 的 AddFolder/RenameFolder 与 ViewModel 复用。
/// </summary>
public static class FolderName
{
    /// <summary>文件夹名称长度上限。全应用统一，唯一固定点。</summary>
    public const int MaxLength = 50;

    /// <summary>
    /// 校验文件夹名称。合法返回 null；非法返回可展示给用户的错误消息。
    /// </summary>
    public static string? Validate(string? rawName)
    {
        var trimmed = rawName?.Trim() ?? "";
        if (trimmed.Length == 0)
            return "文件夹名称不能为空。";
        if (trimmed.Length > MaxLength)
            return $"文件夹名称不能超过 {MaxLength} 个字符。";
        return null;
    }
}