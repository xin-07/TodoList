using System;
using System.Globalization;

namespace TodoList.Models;

/// <summary>
/// 截止日期格式的"单一权威源"助手：定义全应用统一的日期格式常量与严格解析逻辑。
/// 供 ViewModel 复用，拒绝非 `yyyy-MM-dd` 标准格式的输入。
/// </summary>
public static class DueDate
{
    /// <summary>全应用统一的截止日期格式。唯一固定点。</summary>
    public const string Format = "yyyy-MM-dd";

    /// <summary>向用户展示的输入格式提示（与 <see cref="Format"/> 一致）。</summary>
    public const string FormatHint = Format;

    /// <summary>
    /// 严格按 <see cref="Format"/> 解析截止日期字符串。
    /// 合法返回对应日期；非法返回 null（空串/空白亦视为未设置）。
    /// </summary>
    public static DateTime? TryParse(string? rawDate)
    {
        var trimmed = rawDate?.Trim() ?? "";
        if (trimmed.Length == 0)
            return null;

        if (DateTime.TryParseExact(trimmed, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        return null;
    }

    /// <summary>将日期格式化为标准字符串。</summary>
    public static string ToDisplayString(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);
}