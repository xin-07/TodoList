using System;
using System.IO;
using System.Text.Json;

namespace TodoList.Database;

/// <summary>宿主平台。作为可注入边界存在，路径逻辑不直接读宿主环境（Article 11）。</summary>
public enum HostPlatform
{
    Windows,
    Linux,
    MacOs,
}

/// <summary>
/// 配置与数据路径解析的"单一权威源"：config.json 的读取、数据根与数据库绝对路径的推导都集中在此。
/// 程序根、平台与环境变量一律由参数注入，宿主探测只在 <see cref="DetectHostPlatform"/> 一处发生，便于单测。
/// </summary>
public static class DataPaths
{
    /// <summary>相对程序根的数据目录名，config.json 未定义时兜底。</summary>
    public const string DefaultDataDirectory = "data";

    /// <summary>用户级数据根之下的应用目录名，config.json 未定义时兜底。</summary>
    public const string DefaultUserDataDirectoryName = "todolist";

    /// <summary>数据库文件名，config.json 未定义时兜底。</summary>
    public const string DefaultDbFileName = "todo.db";

    /// <summary>config.json 中与"数据"相关的权威配置。</summary>
    public readonly record struct DataSettings(string Directory, string UserDataDirectoryName, string DbFileName);

    /// <summary>探测当前宿主平台；宿主环境读取仅此一处。</summary>
    public static HostPlatform DetectHostPlatform()
    {
        if (OperatingSystem.IsWindows())
            return HostPlatform.Windows;
        if (OperatingSystem.IsMacOS())
            return HostPlatform.MacOs;
        return HostPlatform.Linux;
    }

    /// <summary>
    /// 从程序根读取 config.json（数据目录定义的唯一权威源）；
    /// 文件缺失或字段非法时，仅在缺失字段上回退到 <c>Default*</c> 缺省值。
    /// </summary>
    public static DataSettings ReadDataSettings(string programRoot)
    {
        var configPath = Path.Combine(programRoot, "config.json");
        if (!File.Exists(configPath))
            return new DataSettings(DefaultDataDirectory, DefaultUserDataDirectoryName, DefaultDbFileName);

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var data = doc.RootElement.TryGetProperty("data", out var element) && element.ValueKind == JsonValueKind.Object
            ? element
            : default;

        return new DataSettings(
            ReadString(data, "directory", DefaultDataDirectory),
            ReadString(data, "userDataDirectoryName", DefaultUserDataDirectoryName),
            ReadString(data, "dbFileName", DefaultDbFileName));
    }

    /// <summary>
    /// 解析数据库绝对路径，规则：
    /// 开发模式（能定位项目根）→ <c>项目根/&lt;directory&gt;/&lt;dbFileName&gt;</c>；
    /// 发布 · Linux → <c>$XDG_DATA_HOME/&lt;userDataDirectoryName&gt;/&lt;dbFileName&gt;</c>；
    /// 发布 · Windows/macOS → <c>exe 所在目录/&lt;directory&gt;/&lt;dbFileName&gt;</c>。
    /// Linux 下用户级数据根本身即应用专属目录，不再追加 directory 层（否则会得到无身份含义的 ~/.local/share/data）。
    /// </summary>
    public static string ResolveDatabasePath(
        string programRoot,
        bool isDevelopment,
        HostPlatform platform,
        string? xdgDataHome,
        string? homeDirectory,
        DataSettings settings)
    {
        var directory = !isDevelopment && platform == HostPlatform.Linux
            ? Path.Combine(ResolveUserDataRoot(xdgDataHome, homeDirectory), settings.UserDataDirectoryName)
            : Path.Combine(programRoot, settings.Directory);

        return Path.Combine(directory, settings.DbFileName);
    }

    /// <summary>用户级数据根：$XDG_DATA_HOME（须为绝对路径）优先，否则回退 ~/.local/share。</summary>
    private static string ResolveUserDataRoot(string? xdgDataHome, string? homeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathRooted(xdgDataHome))
            return xdgDataHome;

        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new InvalidOperationException(
                "无法确定用户数据根：XDG_DATA_HOME 与用户主目录均不可用，数据将无处落盘。");
        }

        return Path.Combine(homeDirectory, ".local", "share");
    }

    private static string ReadString(JsonElement element, string property, string fallback)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }

        return fallback;
    }
}