using System;

namespace TodoList.Models;

/// <summary>
/// 周期规则引擎（纯函数，不依赖时钟与外部状态，便于单测与人工推演）。
/// 滚动式语义见 SPEC 阶段三：完成即把截止日期推进到下一周期，不保留发生历史。
/// </summary>
public static class Recurrence
{
    /// <summary>
    /// 由当前截止日期推算下一周期日期：在当前截止日期基础上加 N 个周期单位
    /// （单步滚动，不追赶过去时期；月末由 AddMonths/AddYears 的 clamp 语义兜底）。
    /// </summary>
    public static DateTime NextDue(DateTime due, RecurrenceType type, int interval) => type switch
    {
        RecurrenceType.Daily => due.AddDays(interval),
        RecurrenceType.Weekly => due.AddDays(7L * interval),
        RecurrenceType.Monthly => due.AddMonths(interval),
        RecurrenceType.Yearly => due.AddYears(interval),
        _ => due,
    };

    /// <summary>
    /// 行内规则摘要（如"每周""每 3 天 · 剩 2 次"）；不重复返回 null（调用方按空处理）。
    /// </summary>
    public static string? ToDisplayText(RecurrenceType type, int interval, int? remaining)
    {
        if (type == RecurrenceType.None)
            return null;

        var unit = type switch
        {
            RecurrenceType.Daily => "天",
            RecurrenceType.Weekly => "周",
            RecurrenceType.Monthly => "月",
            _ => "年",
        };

        var text = interval <= 1 ? $"每{unit}" : $"每 {interval} {unit}";
        if (remaining.HasValue)
            text += $" · 剩 {remaining.Value} 次";
        return text;
    }
}
