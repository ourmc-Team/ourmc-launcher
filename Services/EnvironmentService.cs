using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ourmclauncher.Services;

/// <summary>
/// 环境检查服务 - 游戏启动前的环境验证
/// </summary>
public class EnvironmentService
{
    private readonly PathService _pathService;
    private readonly SettingsService _settingsService;

    public EnvironmentService(PathService pathService, SettingsService settingsService)
    {
        _pathService = pathService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// 环境检查结果
    /// </summary>
    public class EnvironmentCheckResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Suggestion { get; set; }
        public EnvironmentCheckLevel Level { get; set; }
    }

    /// <summary>
    /// 环境检查级别
    /// </summary>
    public enum EnvironmentCheckLevel
    {
        Info,       // 信息提示
        Warning,    // 警告但可继续
        Error,      // 错误，无法继续
        Critical    // 严重错误，必须修复
    }

    /// <summary>
    /// 完整的环境检查报告
    /// </summary>
    public class EnvironmentReport
    {
        public bool CanLaunch { get; set; }
        public List<EnvironmentCheckResult> Checks { get; set; } = new();
        public DateTime CheckTime { get; set; } = DateTime.Now;

        public string GetSummary()
        {
            var errors = Checks.Count(c => c.Level == EnvironmentCheckLevel.Error || c.Level == EnvironmentCheckLevel.Critical);
            var warnings = Checks.Count(c => c.Level == EnvironmentCheckLevel.Warning);

            if (errors > 0)
                return $"发现 {errors} 个错误，无法启动游戏";
            if (warnings > 0)
                return $"发现 {warnings} 个警告，建议修复后启动";
            return "环境检查通过，可以启动游戏";
        }
    }

    /// <summary>
    /// 执行完整的环境检查
    /// </summary>
    public EnvironmentReport CheckEnvironment(string versionName)
    {
        var report = new EnvironmentReport();

        try
        {
            // 1. 检查Java环境
            report.Checks.AddRange(CheckJavaEnvironment(versionName));

            // 2. 检查内存设置
            report.Checks.AddRange(CheckMemorySettings());

            // 3. 检查磁盘空间
            report.Checks.AddRange(CheckDiskSpace(versionName));

            // 4. 检查游戏文件完整性
            report.Checks.AddRange(CheckGameFiles(versionName));

            // 5. 检查依赖文件
            report.Checks.AddRange(CheckDependencies(versionName));

            // 判断是否可以启动
            report.CanLaunch = !report.Checks.Any(c =>
                c.Level == EnvironmentCheckLevel.Error ||
                c.Level == EnvironmentCheckLevel.Critical);
        }
        catch (Exception ex)
        {
            report.Checks.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"环境检查过程中发生错误: {ex.Message}",
                Level = EnvironmentCheckLevel.Critical
            });
            report.CanLaunch = false;
        }

        return report;
    }

    /// <summary>
    /// 检查Java环境
    /// </summary>
    private List<EnvironmentCheckResult> CheckJavaEnvironment(string versionName)
    {
        var results = new List<EnvironmentCheckResult>();

        try
        {
            // 检查Java路径
            var javaPath = _settingsService.GetJavaPath();
            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "未找到Java运行环境",
                    Suggestion = "请在设置中配置Java路径，或点击自动检测",
                    Level = EnvironmentCheckLevel.Critical
                });
                return results;
            }

            // 检查Java版本
            var javaVersion = GetJavaVersion(javaPath);
            if (javaVersion == null)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "无法检测Java版本",
                    Suggestion = "请确保Java安装正确，或尝试重新安装Java",
                    Level = EnvironmentCheckLevel.Error
                });
                return results;
            }

            // 检查版本兼容性
            var requiredJavaVersion = GetRequiredJavaVersion(versionName);
            if (!IsJavaVersionCompatible(javaVersion, requiredJavaVersion))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Java版本不兼容 (当前: {javaVersion}, 需要: {requiredJavaVersion}+)",
                    Suggestion = "请升级Java版本或选择兼容的游戏版本",
                    Level = EnvironmentCheckLevel.Error
                });
            }
            else
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = $"Java版本检查通过 (版本: {javaVersion})",
                    Level = EnvironmentCheckLevel.Info
                });
            }

            // 检查Java架构
            if (!Is64BitJava(javaPath))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "检测到32位Java，性能受限",
                    Suggestion = "建议安装64位Java以获得更好的性能和更大的内存支持",
                    Level = EnvironmentCheckLevel.Warning
                });
            }
        }
        catch (Exception ex)
        {
            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"Java环境检查失败: {ex.Message}",
                Level = EnvironmentCheckLevel.Error
            });
        }

        return results;
    }

    /// <summary>
    /// 检查内存设置
    /// </summary>
    private List<EnvironmentCheckResult> CheckMemorySettings()
    {
        var results = new List<EnvironmentCheckResult>();

        try
        {
            var maxMemory = _settingsService.GetMaxMemory();
            var systemMemory = GetSystemMemory();

            if (maxMemory > systemMemory)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"内存分配超出系统可用内存 (分配: {maxMemory}MB, 系统: {systemMemory}MB)",
                    Suggestion = $"建议将最大内存设置为 {Math.Min(systemMemory - 512, maxMemory)}MB 或更少",
                    Level = EnvironmentCheckLevel.Error
                });
            }
            else if (maxMemory > systemMemory * 0.8)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = $"内存分配接近系统上限 (分配: {maxMemory}MB, 系统: {systemMemory}MB)",
                    Suggestion = "建议适当减少内存分配以避免系统卡顿",
                    Level = EnvironmentCheckLevel.Warning
                });
            }
            else if (maxMemory < 512)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"内存分配过低 (当前: {maxMemory}MB)",
                    Suggestion = "建议至少分配512MB内存以确保游戏正常运行",
                    Level = EnvironmentCheckLevel.Warning
                });
            }
            else
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = $"内存设置合理 (分配: {maxMemory}MB)",
                    Level = EnvironmentCheckLevel.Info
                });
            }
        }
        catch (Exception ex)
        {
            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"内存设置检查失败: {ex.Message}",
                Level = EnvironmentCheckLevel.Warning
            });
        }

        return results;
    }

    /// <summary>
    /// 检查磁盘空间
    /// </summary>
    private List<EnvironmentCheckResult> CheckDiskSpace(string versionName)
    {
        var results = new List<EnvironmentCheckResult>();

        try
        {
            var gameDir = _pathService.GameDir;
            var pathRoot = Path.GetPathRoot(gameDir);
            if (string.IsNullOrEmpty(pathRoot))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "无法获取磁盘根路径",
                    Level = EnvironmentCheckLevel.Error
                });
                return results;
            }

            var driveInfo = new DriveInfo(pathRoot);

            var requiredSpace = CalculateRequiredSpace(versionName);
            var availableSpace = driveInfo.AvailableFreeSpace / (1024 * 1024); // MB

            if (availableSpace < requiredSpace)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"磁盘空间不足 (需要: {requiredSpace}MB, 可用: {availableSpace}MB)",
                    Suggestion = "请清理磁盘空间或选择其他安装位置",
                    Level = EnvironmentCheckLevel.Critical
                });
            }
            else if (availableSpace < requiredSpace * 2)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = $"磁盘空间紧张 (需要: {requiredSpace}MB, 可用: {availableSpace}MB)",
                    Suggestion = "建议清理部分磁盘空间以确保游戏正常运行",
                    Level = EnvironmentCheckLevel.Warning
                });
            }
            else
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = $"磁盘空间充足 (可用: {availableSpace}MB)",
                    Level = EnvironmentCheckLevel.Info
                });
            }
        }
        catch (Exception ex)
        {
            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"磁盘空间检查失败: {ex.Message}",
                Level = EnvironmentCheckLevel.Warning
            });
        }

        return results;
    }

    /// <summary>
    /// 检查游戏文件完整性
    /// </summary>
    private List<EnvironmentCheckResult> CheckGameFiles(string versionName)
    {
        var results = new List<EnvironmentCheckResult>();

        try
        {
            var versionDir = _pathService.GetVersionDir(versionName);
            var jarPath = _pathService.GetVersionJarPath(versionName);
            var jsonPath = _pathService.GetVersionJsonPath(versionName);

            // 检查版本目录
            if (!Directory.Exists(versionDir))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "游戏版本目录不存在",
                    Suggestion = "请先下载游戏版本",
                    Level = EnvironmentCheckLevel.Critical
                });
                return results;
            }

            // 检查主JAR文件
            if (!File.Exists(jarPath))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "游戏主JAR文件缺失",
                    Suggestion = "请重新下载游戏版本",
                    Level = EnvironmentCheckLevel.Critical
                });
                return results;
            }

            // 检查版本JSON文件
            if (!File.Exists(jsonPath))
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "版本清单文件缺失",
                    Suggestion = "请重新下载游戏版本",
                    Level = EnvironmentCheckLevel.Error
                });
            }

            if (File.Exists(jsonPath))
            {
                var librariesMissing = CheckLibraries(jsonPath);
                if (librariesMissing.Count > 0)
                {
                    results.Add(new EnvironmentCheckResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"缺失 {librariesMissing.Count} 个游戏库文件",
                        Suggestion = "请重新下载游戏版本或手动修复库文件",
                        Level = EnvironmentCheckLevel.Error
                    });
                }
            }

            if (results.Count == 0)
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = true,
                    ErrorMessage = "游戏文件检查通过",
                    Level = EnvironmentCheckLevel.Info
                });
            }
        }
        catch (Exception ex)
        {
            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"游戏文件检查失败: {ex.Message}",
                Level = EnvironmentCheckLevel.Warning
            });
        }

        return results;
    }

    /// <summary>
    /// 检查依赖文件
    /// </summary>
    private List<EnvironmentCheckResult> CheckDependencies(string versionName)
    {
        var results = new List<EnvironmentCheckResult>();

        try
        {
            var gameDir = _pathService.GameDir;
            var assetsDir = Path.Combine(gameDir, "assets");
            // 检查资源文件
            if (!Directory.Exists(assetsDir) || !Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories).Any())
            {
                results.Add(new EnvironmentCheckResult
                {
                    IsSuccess = false,
                    ErrorMessage = "游戏资源文件缺失",
                    Suggestion = "首次启动游戏时会自动下载资源文件",
                    Level = EnvironmentCheckLevel.Warning
                });
            }

            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = true,
                ErrorMessage = "依赖文件检查完成",
                Level = EnvironmentCheckLevel.Info
            });
        }
        catch (Exception ex)
        {
            results.Add(new EnvironmentCheckResult
            {
                IsSuccess = false,
                ErrorMessage = $"依赖文件检查失败: {ex.Message}",
                Level = EnvironmentCheckLevel.Warning
            });
        }

        return results;
    }

    #region 辅助方法

    /// <summary>
    /// 获取Java版本
    /// </summary>
    private string? GetJavaVersion(string javaPath)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var output = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // 解析版本号
            if (output.Contains("version"))
            {
                var versionLine = output.Split('\n').FirstOrDefault(line => line.Contains("version"));
                if (versionLine != null)
                {
                    var versionStart = versionLine.IndexOf('"') + 1;
                    var versionEnd = versionLine.IndexOf('"', versionStart);
                    if (versionStart > 0 && versionEnd > versionStart)
                    {
                        var fullVersion = versionLine.Substring(versionStart, versionEnd - versionStart);
                        // 提取主版本号 (如 "17.0.0" -> "17")
                        var majorVersion = fullVersion.Split('.')[0];
                        if (int.TryParse(majorVersion, out int majorVer))
                        {
                            return majorVer >= 9 ? majorVersion : "8"; // Java 8 及以下特殊处理
                        }
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查是否为64位Java
    /// </summary>
    private bool Is64BitJava(string javaPath)
    {
        try
        {
            // 通过检查Java路径中的"64"关键字来判断
            var javaDir = Path.GetDirectoryName(javaPath);
            if (javaDir != null && javaDir.Contains("64"))
                return true;

            // 或者通过系统属性检查
            var processInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-XshowSettings:properties -version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

            var output = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return output.Contains("os.arch=amd64") || output.Contains("os.arch=x86_64");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取所需Java版本
    /// </summary>
    private int GetRequiredJavaVersion(string versionName)
    {
        var declaredVersion = TryGetDeclaredJavaVersion(versionName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (declaredVersion.HasValue)
        {
            return declaredVersion.Value;
        }

        return GetFallbackJavaVersion(versionName);
    }

    private int? TryGetDeclaredJavaVersion(string versionName, HashSet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(versionName) || !visited.Add(versionName))
        {
            return null;
        }

        try
        {
            var jsonPath = _pathService.GetVersionJsonPath(versionName);
            if (!File.Exists(jsonPath))
            {
                return null;
            }

            var version = JsonNode.Parse(File.ReadAllText(jsonPath));
            if (int.TryParse(version?["javaVersion"]?["majorVersion"]?.ToString(), out var majorVersion))
            {
                return majorVersion;
            }

            var parent = version?["inheritsFrom"]?.ToString();
            return string.IsNullOrWhiteSpace(parent)
                ? null
                : TryGetDeclaredJavaVersion(parent, visited);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取版本 Java 要求失败: {ex.Message}");
            return null;
        }
    }

    internal static int GetFallbackJavaVersion(string versionName)
    {
        var match = Regex.Match(versionName ?? "", @"1\.(?<minor>\d+)(?:\.(?<patch>\d+))?");
        if (!match.Success || !int.TryParse(match.Groups["minor"].Value, out var minor))
        {
            return 8;
        }

        _ = int.TryParse(match.Groups["patch"].Value, out var patch);
        if (minor > 20 || minor == 20 && patch >= 5)
        {
            return 21;
        }

        if (minor >= 18)
        {
            return 17;
        }

        return minor == 17 ? 16 : 8;
    }

    /// <summary>
    /// 检查Java版本兼容性
    /// </summary>
    private bool IsJavaVersionCompatible(string? currentVersion, int requiredVersion)
    {
        if (!int.TryParse(currentVersion, out int currentVer))
            return false;

        return currentVer >= requiredVersion;
    }

    /// <summary>
    /// 获取系统内存
    /// </summary>
    private long GetSystemMemory()
    {
        try
        {
            var mc = new Microsoft.VisualBasic.Devices.ComputerInfo();
            var totalMemory = (long)(mc.TotalPhysicalMemory / (1024 * 1024)); // 转换为MB
            return totalMemory;
        }
        catch
        {
            // 如果无法获取，返回保守估计值8GB
            return 8192;
        }
    }

    /// <summary>
    /// 计算所需磁盘空间
    /// </summary>
    private long CalculateRequiredSpace(string versionName)
    {
        // 基础游戏约500MB，加上资源文件和库文件
        // 保守估计需要2GB空间
        return 2048; // MB
    }

    /// <summary>
    /// 检查库文件
    /// </summary>
    private List<string> CheckLibraries(string jsonPath)
    {
        var missingLibs = new List<string>();

        try
        {
            var jsonContent = File.ReadAllText(jsonPath);
            var jsonObj = JsonNode.Parse(jsonContent) as JsonObject;

            // 使用索引器访问属性
            var librariesNode = jsonObj?["libraries"];
            if (librariesNode != null && librariesNode is JsonArray librariesArray)
            {
                var librariesDir = Path.Combine(_pathService.GameDir, "libraries");

                foreach (var lib in librariesArray)
                {
                    if (lib is JsonObject libObj)
                    {
                        if (libObj["rules"] is JsonArray rules && !AreRulesAllowed(rules))
                        {
                            continue;
                        }

                        var libName = libObj["name"]?.ToString() ?? "未知库";
                        var libPath = libObj["downloads"]?["artifact"]?["path"]?.ToString();
                        if (string.IsNullOrWhiteSpace(libPath))
                        {
                            libPath = ConvertMavenLibPath(libName);
                        }

                        var fullPath = Path.Combine(
                            librariesDir,
                            libPath.Replace('/', Path.DirectorySeparatorChar));

                        if (!File.Exists(fullPath))
                        {
                            missingLibs.Add(libName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查库文件失败: {ex.Message}");
        }

        return missingLibs;
    }

    private static bool AreRulesAllowed(JsonArray rules)
    {
        var allowed = false;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule)
            {
                continue;
            }

            var osName = rule["os"]?["name"]?.ToString();
            if (!string.IsNullOrEmpty(osName) && !string.Equals(osName, "windows", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            allowed = string.Equals(rule["action"]?.ToString(), "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    /// <summary>
    /// 转换Maven库路径
    /// </summary>
    private string ConvertMavenLibPath(string libName)
    {
        // 例如: "com.google.guava:guava:31.0.1-jre" -> "com/google/guava/guava/31.0.1-jre/"
        var parts = libName.Split(':');
        if (parts.Length >= 3)
        {
            var groupId = parts[0].Replace('.', '/');
            var artifactId = parts[1];
            var version = parts[2];
            return $"{groupId}/{artifactId}/{version}/{artifactId}-{version}.jar";
        }
        return libName;
    }

    #endregion
}
