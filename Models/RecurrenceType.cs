namespace TodoList.Models;

/// <summary>
/// 周期规则的"单一权威源"枚举：全应用统一使用，禁止在别处散落同义定义。
/// None 之外的取值开启滚动式续期（完成即推进到下一期，见 SPEC 阶段三）。
/// </summary>
public enum RecurrenceType
{
    /// <summary>不重复（普通任务）。</summary>
    None,

    /// <summary>按天重复：下一期 = 截止日期 + N 天。</summary>
    Daily,

    /// <summary>按周重复：下一期 = 截止日期 + N 周。</summary>
    Weekly,

    /// <summary>按月重复：下一期 = 截止日期 + N 月（月末溢出由 AddMonths 自动 clamp）。</summary>
    Monthly,

    /// <summary>按年重复：下一期 = 截止日期 + N 年。</summary>
    Yearly,
}
