using System;
using System.Collections.Generic;

using System.Text.Json.Serialization;

namespace ourmclauncher.Models;

/// <summary>
/// 游戏版本信息
/// </summary>
public class GameVersion
{
    /// <summary>
    /// 版本ID（通常与Name相同）
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 版本名称（如 1.20.1）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 版本类型（如 正式版、Forge、Fabric）
    /// </summary>
    public string Type { get; set; } = "";
}

/// <summary>
/// 远程版本信息（从Mojang官方获取）
/// </summary>
public class RemoteVersion
{
    /// <summary>
    /// 版本ID（如 1.20.1）
    /// </summary>
    public string Id { get; set; } = "";
    
    /// <summary>
    /// 版本类型（release/snapshot）
    /// </summary>
    public string Type { get; set; } = "";
    
    /// <summary>
    /// 版本清单URL
    /// </summary>
    public string Url { get; set; } = "";
    
    /// <summary>
    /// 发布时间
    /// </summary>
    public string ReleaseTime { get; set; } = "";
}

/// <summary>
/// 用户信息
/// </summary>
public class UserInfo
{
    /// <summary>
    /// 用户邮箱
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// 头像URL
    /// </summary>
    public string Avatar { get; set; } = string.Empty;
}

/// <summary>
/// 登录结果
/// </summary>
public class LoginResult
{
    /// <summary>
    /// 是否登录成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string Message { get; set; } = "";
    
    /// <summary>
    /// 用户信息
    /// </summary>
    public UserInfo? User { get; set; }
}

/// <summary>
/// 用户资料（包含皮肤信息）
/// </summary>
public class UserProfile
{
    /// <summary>
    /// 用户UUID
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户名
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 属性（皮肤等）
    /// </summary>
    public Dictionary<string, TextureProperty> Properties { get; set; } = new();
}

/// <summary>
/// 纹理属性
/// </summary>
public class TextureProperty
{
    /// <summary>
    /// 属性名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 属性值（Base64编码）
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 应用设置
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Java可执行文件路径
    /// </summary>
    public string JavaPath { get; set; } = "";
    
    /// <summary>
    /// 最大内存（MB）
    /// </summary>
    public int MaxMemory { get; set; } = 2048;
    
    /// <summary>
    /// 游戏目录路径
    /// </summary>
    public string GameDirectory { get; set; }
    
    /// <summary>
    /// 玩家名称（离线模式）
    /// </summary>
    public string PlayerName { get; set; } = "Player";

    /// <summary>
    /// 背景图片索引 (0=back.jpg, 1=back1.jpg, ..., 6=back6.jpg)
    /// </summary>
    public int BackgroundIndex { get; set; } = 0;

    /// <summary>
    /// 全屏模式
    /// </summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// 窗口宽度
    /// </summary>
    public int WindowWidth { get; set; } = 854;

    /// <summary>
    /// 窗口高度
    /// </summary>
    public int WindowHeight { get; set; } = 480;

    /// <summary>
    /// 自定义 JVM 参数（如 -XX:+UseG1GC -Dfile.encoding=UTF-8）
    /// </summary>
    public string CustomJvmArgs { get; set; } = "";

    [JsonIgnore]
    public string AIApiKey { get; set; } = "";

    public string ProtectedAIApiKey { get; set; } = "";

    [JsonPropertyName("AIApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAIApiKey { get; set; }

    public string AIProvider { get; set; } = "OpenAI";

    /// <summary>
    /// 是否显示AI欢迎引导
    /// </summary>
    public bool ShowAIWelcome { get; set; } = true;

    public AppSettings()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        GameDirectory = Path.Combine(appData, ".minecraft");
    }
}

/// <summary>
/// 通知项
/// </summary>
public class NotificationItem
{
    /// <summary>
    /// 通知ID
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// 通知类型（success/error/info/warning）
    /// </summary>
    public string Type { get; set; } = "info";
    
    /// <summary>
    /// 通知消息
    /// </summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// 版本类型枚举
/// </summary>
public enum VersionType
{
    /// <summary>
    /// 原版正式版
    /// </summary>
    Release,
    
    /// <summary>
    /// 快照版本
    /// </summary>
    Snapshot,
    
    /// <summary>
    /// Forge模组加载器
    /// </summary>
    Forge,
    
    /// <summary>
    /// Fabric模组加载器
    /// </summary>
    Fabric,
    
    /// <summary>
    /// Quilt模组加载器
    /// </summary>
    Quilt,
    
    /// <summary>
    /// OptiFine优化模组
    /// </summary>
    OptiFine,
    
    /// <summary>
    /// 其他模组版本
    /// </summary>
    Modded
}

/// <summary>
/// 模组加载器类型
/// </summary>
public enum LoaderType
{
    /// <summary>
    /// 原版（无模组加载器）
    /// </summary>
    Vanilla,

    /// <summary>
    /// Forge
    /// </summary>
    Forge,

    /// <summary>
    /// Fabric
    /// </summary>
    Fabric
}

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 崩溃
    /// </summary>
    Crash
}

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// 日志消息
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Info;
}

/// <summary>
/// 模组信息
/// </summary>
public class ModInfo
{
    /// <summary>
    /// 模组文件名
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// 模组显示名称
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 模组版本
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// 模组描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 模组作者
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// 模组文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 是否已启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 模组图标（Base64）
    /// </summary>
    public string? Icon { get; set; }
}

/// <summary>
/// 服务器信息
/// </summary>
public class ServerInfo
{
    /// <summary>
    /// 服务器ID（唯一标识）
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 服务器显示名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 服务器地址（IP:端口）
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// 服务器版本（适用的 MC 版本）
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// 服务器描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 添加时间
    /// </summary>
    public DateTime AddedTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 账号类型
/// </summary>
public enum AccountType
{
    /// <summary>
    /// 离线账号
    /// </summary>
    Offline = 0,

    /// <summary>
    /// 微软账号
    /// </summary>
    Microsoft = 1,

    /// <summary>
    /// 皮肤站账号
    /// </summary>
    SkinSite = 2
}

/// <summary>
/// 账号信息
/// </summary>
public class Account
{
    /// <summary>
    /// 账号ID（唯一标识）
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 账号类型
    /// </summary>
    public AccountType Type { get; set; } = AccountType.Offline;

    /// <summary>
    /// 用户邮箱/用户名
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = "";

    /// <summary>
    /// 用户名（游戏内显示）
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// 头像URL
    /// </summary>
    public string Avatar { get; set; } = "";

    /// <summary>
    /// 是否为默认账号
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// 添加时间
    /// </summary>
    public DateTime AddedTime { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string AccessToken { get; set; } = "";

    [JsonIgnore]
    public string RefreshToken { get; set; } = "";

    public string ProtectedAccessToken { get; set; } = "";

    public string ProtectedRefreshToken { get; set; } = "";

    [JsonPropertyName("AccessToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAccessToken { get; set; }

    [JsonPropertyName("RefreshToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyRefreshToken { get; set; }

    /// <summary>
    /// 令牌过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// UUID（微软账号）
    /// </summary>
    public string Uuid { get; set; } = "";

    public string Xuid { get; set; } = "";
}

/// <summary>
/// 游戏实例信息
/// </summary>
public class GameInstance
{
    /// <summary>
    /// 实例ID（唯一标识）
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 实例名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 实例类型
    /// </summary>
    public InstanceType Type { get; set; } = InstanceType.Custom;

    /// <summary>
    /// 游戏版本ID
    /// </summary>
    public string VersionId { get; set; } = "";

    /// <summary>
    /// 实例图标（Base64编码）
    /// </summary>
    public string Icon { get; set; } = "";

    /// <summary>
    /// 实例描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后游玩时间
    /// </summary>
    public DateTime LastPlayedAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// 总游玩时间（秒）
    /// </summary>
    public long TotalPlayTime { get; set; } = 0;

    /// <summary>
    /// JVM参数
    /// </summary>
    public string JavaArgs { get; set; } = "";

    /// <summary>
    /// 最大内存（MB）
    /// </summary>
    public int MaxMemory { get; set; } = 2048;

    /// <summary>
    /// 是否启用高级设置
    /// </summary>
    public bool UseAdvancedSettings { get; set; } = false;
}

/// <summary>
/// 实例类型
/// </summary>
public enum InstanceType
{
    /// <summary>
    /// 自定义实例
    /// </summary>
    Custom = 0,

    /// <summary>
    /// 原版实例
    /// </summary>
    Vanilla = 1,

    /// <summary>
    /// Modded实例
    /// </summary>
    Modded = 2,

    /// <summary>
    /// 服务器实例
    /// </summary>
    Server = 3,

    /// <summary>
    /// 集成实例
    /// </summary>
    Integrated = 4,

    /// <summary>
    /// 第三方实例
    /// </summary>
    ThirdParty = 5
}
