using System;
using System.IO;
using ourmclauncher.Services;

namespace ourmclauncher.Services;

/// <summary>
/// 路径管理服务 - 统一处理所有文件系统路径
/// </summary>
public class PathService
{
    private readonly SettingsService _settings;
    
    /// <summary>
    /// OML启动器数据目录 (%APPDATA%\.minecraft\oml)
    /// </summary>
    public string AppDataDir { get; }
    
    /// <summary>
    /// 默认游戏目录 (%APPDATA%\.minecraft)
    /// </summary>
    public string DefaultGameDir { get; }
    
    /// <summary>
    /// 当前游戏目录（从设置获取或使用默认值）
    /// </summary>
    public string GameDir
    {
        get
        {
            var configuredDirectory = _settings.GetGameDirectory();
            return string.IsNullOrWhiteSpace(configuredDirectory)
                ? DefaultGameDir
                : configuredDirectory;
        }
    }
    
    public PathService(SettingsService settings)
    {
        _settings = settings;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DefaultGameDir = Path.Combine(appData, ".minecraft");
        AppDataDir = Path.Combine(DefaultGameDir, "oml");
        
        // 确保目录存在
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(GameDir);
    }
    
    /// <summary>
    /// 获取版本目录路径
    /// </summary>
    public string GetVersionDir(string versionName)
    {
        return Path.Combine(GameDir, "versions", versionName);
    }

    /// <summary>
    /// 获取版本根目录路径
    /// </summary>
    public string GetVersionsDir()
    {
        return Path.Combine(GameDir, "versions");
    }
    
    /// <summary>
    /// 获取库文件目录路径
    /// </summary>
    public string GetLibrariesDir()
    {
        return Path.Combine(GameDir, "libraries");
    }
    
    /// <summary>
    /// 获取资源目录路径
    /// </summary>
    public string GetAssetsDir()
    {
        return Path.Combine(GameDir, "assets");
    }
    
    /// <summary>
    /// 获取资源索引目录路径
    /// </summary>
    public string GetAssetIndexesDir()
    {
        return Path.Combine(GetAssetsDir(), "indexes");
    }
    
    /// <summary>
    /// 获取Native库目录路径
    /// </summary>
    public string GetNativesDir(string versionName)
    {
        return Path.Combine(GetVersionDir(versionName), "natives");
    }
    
    /// <summary>
    /// 获取版本JAR文件路径
    /// </summary>
    public string GetVersionJarPath(string versionName)
    {
        return Path.Combine(GetVersionDir(versionName), $"{versionName}.jar");
    }
    
    /// <summary>
    /// 获取版本JSON文件路径
    /// </summary>
    public string GetVersionJsonPath(string versionName)
    {
        return Path.Combine(GetVersionDir(versionName), $"{versionName}.json");
    }
    
    /// <summary>
    /// 获取日志文件路径
    /// </summary>
    public string GetLogFilePath()
    {
        return Path.Combine(AppDataDir, "launcher.log");
    }
    
    /// <summary>
    /// 获取会话文件路径
    /// </summary>
    public string GetSessionFilePath()
    {
        return Path.Combine(AppDataDir, "session.dat");
    }
    
    /// <summary>
    /// 确保目录存在
    /// </summary>
    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
