namespace TodoList.Models;

/// <summary>
/// 任务标题的"单一权威源"校验助手：Trim + 非空 + MaxLength。
/// 供 Repository 的 Add/Rename 与 ViewModel 复用，拒绝空白与超长。
/// </summary>
public static class TaskTitle
{
    /// <summary>标题长度上限。全应用统一，唯一固定点。</summary>
    public const int MaxLength = 100;

    /// <summary>
    /// 校验标题。合法返回 null；非法返回可展示给用户的错误消息。
    /// </summary>
    public static string? Validate(string? rawTitle)
    {
        var trimmed = rawTitle?.Trim() ?? "";
        if (trimmed.Length == 0)
            return "任务标题不能为空。";
        if (trimmed.Length > MaxLength)
            return $"任务标题不能超过 {MaxLength} 个字符。";
        return null;
    }
}