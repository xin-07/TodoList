namespace TodoList.Models;

/// <summary>
/// 界面主题的"单一权威源"枚举：全应用统一使用，禁止在别处散落同义定义。
/// System 为默认（跟随操作系统明暗设置）；config.json appearance.theme 存储其字符串形式。
/// </summary>
public enum UiTheme
{
    /// <summary>跟随操作系统明暗设置（默认，config.json 缺省即此值）。</summary>
    System,

    /// <summary>浅色。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,
}
