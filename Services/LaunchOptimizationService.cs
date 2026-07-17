using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 启动优化服务 - 基于PCL/PCL2的启动优化技术
/// </summary>
public class LaunchOptimizationService
{
    private readonly SettingsService _settings;

    public LaunchOptimizationService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// 获取优化后的启动参数
    /// </summary>
    public string GetOptimizedLaunchArguments(string javaPath, string gameDir, string versionDir, string jarPath,
        string jsonPath, string versionName, string? playerName, int maxMemory, string customJvmArgs = "")
    {
        var baseArgs = BuildBasicLaunchArguments(javaPath, gameDir, versionDir, jarPath, jsonPath, versionName, playerName, maxMemory, customJvmArgs);

        // 添加性能优化参数
        var optimizedArgs = ApplyPerformanceOptimizations(baseArgs, versionName);

        return optimizedArgs;
    }

    /// <summary>
    /// 构建基础启动参数
    /// </summary>
    private string BuildBasicLaunchArguments(string javaPath, string gameDir, string versionDir, string jarPath,
        string jsonPath, string versionName, string? playerName, int maxMemory, string customJvmArgs)
    {
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var mainClass = root.GetProperty("mainClass").GetString();
            var classPath = BuildClassPath(gameDir, versionDir, jarPath, root);

            var playerNameValue = playerName ?? "Player";
            var assetsDir = Path.Combine(gameDir, "assets");

            var assetId = "legacy";
            if (root.TryGetProperty("assetIndex", out var assetIndex))
            {
                assetId = assetIndex.GetProperty("id").GetString() ?? "legacy";
            }

            // 基础JVM参数
            var jvmArgs = BuildOptimizedJvmArgs(maxMemory, gameDir, versionDir);

            // 游戏参数
            var gameArgs = $"--username {playerNameValue} " +
                          $"--version \"{versionName}\" " +
                          $"--gameDir \"{gameDir}\" " +
                          $"--assetsDir \"{assetsDir}\" " +
                          $"--assetIndex {assetId} " +
                          $"--uuid 0 " +
                          $"--accessToken 0 " +
                          $"--userType mojang " +
                          $"--versionType \"oml\"";

            return $"{jvmArgs} -cp \"{classPath}\" \"{mainClass}\" {gameArgs}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"构建启动参数失败: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 构建优化的JVM参数
    /// </summary>
    private string BuildOptimizedJvmArgs(int maxMemory, string gameDir, string versionDir)
    {
        var args = new List<string>();

        // 内存设置
        var systemMemory = GetSystemMemory();
        var optimizedMaxMemory = OptimizeMemoryAllocation(maxMemory, systemMemory);
        var optimizedMinMemory = Math.Min(512, optimizedMaxMemory / 4);

        args.Add($"-Xmx{optimizedMaxMemory}M");
        args.Add($"-Xms{optimizedMinMemory}M");

        // 性能优化参数（基于PCL的优化）
        args.Add("-XX:+UseG1GC");                    // 使用G1垃圾回收器
        args.Add("-XX:+UnlockExperimentalVMOptions"); // 解锁实验性VM选项
        args.Add("-XX:+DisableExplicitGC");         // 禁用显式GC
        args.Add("-XX:+UseStringDeduplication");     // 字符串去重优化
        args.Add("-XX:+UseCompressedOops");          // 使用压缩普通对象指针
        args.Add("-XX:+OptimizeStringConcat");       // 优化字符串连接

        // 系统架构优化
        if (Environment.Is64BitProcess)
        {
            args.Add("-XX:+UseCompressedClassPointers"); // 64位压缩类指针
        }

        // Native库路径
        var nativeDir = Path.Combine(versionDir, "natives");
        args.Add($"-Djava.library.path=\"{nativeDir}\"");

        // 服务器优化（如果有足够内存）
        if (systemMemory >= 8 * 1024) // 8GB以上
        {
            args.Add("-XX:+UseLargePages");            // 使用大内存页
            args.Add("-XX:+AggressiveOpts");           // 激进优化
        }

        // 自定义JVM参数（用户配置的参数会覆盖默认参数）
        var customArgs = _settings.GetCustomJvmArgs();
        if (!string.IsNullOrWhiteSpace(customArgs))
        {
            args.Add(customArgs.Trim());
        }

        return string.Join(" ", args);
    }

    /// <summary>
    /// 应用性能优化
    /// </summary>
    private string ApplyPerformanceOptimizations(string baseArgs, string versionName)
    {
        // 根据游戏版本应用不同的优化策略
        if (versionName.Contains("1.8") || versionName.Contains("1.7"))
        {
            // 旧版本优化
            baseArgs += " -Dsun.java2d.d3d=false"; // 禁用D3D加速（旧版本兼容）
        }
        else
        {
            // 新版本优化
            baseArgs += " -Dsun.java2d.d3d=true";  // 启用D3D加速
            baseArgs += " -Dsun.java2d.opengl=true"; // 启用OpenGL加速
        }

        // FPS优化
        baseArgs += " -Dminecraft.launcher.brand=OMLLauncher";
        baseArgs += " -Dminecraft.launcher.version=2.0";

        return baseArgs;
    }

    /// <summary>
    /// 优化内存分配
    /// </summary>
    private int OptimizeMemoryAllocation(int requestedMemory, int systemMemory)
    {
        // 系统内存保留2GB给系统
        var availableMemory = Math.Max(0, systemMemory - 2048);

        // 用户请求的内存不能超过可用内存的80%
        var maxAllowedMemory = (int)(availableMemory * 0.8);

        // 确保至少有512MB内存
        var minRequiredMemory = 512;

        var optimizedMemory = requestedMemory;

        // 如果用户请求的内存超过可用内存，自动调整
        if (optimizedMemory > maxAllowedMemory)
        {
            optimizedMemory = maxAllowedMemory;
        }

        // 确保不低于最小要求
        if (optimizedMemory < minRequiredMemory)
        {
            optimizedMemory = minRequiredMemory;
        }

        // 对齐到256MB边界
        return ((optimizedMemory + 255) / 256) * 256;
    }

    /// <summary>
    /// 获取系统内存大小（MB）
    /// </summary>
    private int GetSystemMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows系统获取内存信息
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return (int)(memStatus.ullTotalPhys / (1024 * 1024));
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Linux/Mac系统获取内存信息
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = "-c \"free -m | grep Mem | awk '{print $2}'\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (int.TryParse(output.Trim(), out var memory))
                {
                    return memory;
                }
            }

            // 默认返回4GB
            return 4096;
        }
        catch
        {
            return 4096;
        }
    }

    /// <summary>
    /// 构建类路径
    /// </summary>
    private string BuildClassPath(string gameDir, string versionDir, string jarPath, JsonElement root)
    {
        var cpEntries = new List<string> { jarPath };

        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var lib in libraries.EnumerateArray())
            {
                if (lib.TryGetProperty("rules", out var rules))
                {
                    if (!CheckRules(rules)) continue;
                }

                if (lib.TryGetProperty("downloads", out var downloads))
                {
                    if (downloads.TryGetProperty("artifact", out var artifact))
                    {
                        if (artifact.TryGetProperty("path", out var path))
                        {
                            var libPath = Path.Combine(gameDir, "libraries", path.GetString()!);
                            if (File.Exists(libPath))
                            {
                                cpEntries.Add(libPath);
                            }
                        }
                    }
                }
            }
        }

        return string.Join(";", cpEntries);
    }

    /// <summary>
    /// 检查库规则
    /// </summary>
    private bool CheckRules(JsonElement rules)
    {
        var allowed = true;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.GetProperty("action").GetString();
            if (action == "disallow") allowed = false;
            else if (action == "allow") allowed = true;
        }
        return allowed;
    }

    /// <summary>
    /// 获取推荐的内存设置
    /// </summary>
    public (int minMemory, int recommendedMemory, int maxMemory) GetRecommendedMemorySettings()
    {
        var systemMemory = GetSystemMemory();

        int minMemory, recommendedMemory, maxMemory;

        if (systemMemory <= 4096) // 4GB及以下
        {
            minMemory = 512;
            recommendedMemory = 1024;
            maxMemory = 2048;
        }
        else if (systemMemory <= 8192) // 4-8GB
        {
            minMemory = 512;
            recommendedMemory = 2048;
            maxMemory = 4096;
        }
        else if (systemMemory <= 16384) // 8-16GB
        {
            minMemory = 1024;
            recommendedMemory = 4096;
            maxMemory = 8192;
        }
        else // 16GB以上
        {
            minMemory = 2048;
            recommendedMemory = 6144;
            maxMemory = 12288;
        }

        return (minMemory, recommendedMemory, maxMemory);
    }

    #region Windows API

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    #endregion
}