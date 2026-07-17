using System;
using System.Collections.Generic;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 启动预设服务 - 管理游戏启动配置预设
    /// 提供保守、跨常见 Java 版本可用的启动配置
/// </summary>
public class LaunchPresetService
{
    /// <summary>
    /// 启动预设类型
    /// </summary>
    public enum PresetType
    {
        /// <summary>
        /// 性能优先 - 最大化游戏性能
        /// </summary>
        Performance,

        /// <summary>
        /// 兼容优先 - 最大化兼容性
        /// </summary>
        Compatibility,

        /// <summary>
        /// 平衡 - 性能与兼容性平衡
        /// </summary>
        Balanced,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom
    }

    /// <summary>
    /// 启动配置预设
    /// </summary>
    public class LaunchPreset
    {
        public string Name { get; set; } = "";
        public PresetType Type { get; set; }
        public string Description { get; set; } = "";
        public int MinMemory { get; set; }
        public int RecommendedMemory { get; set; }
        public string JvmArgs { get; set; } = "";
        public bool EnableG1GC { get; set; }
        public bool EnableOptimizations { get; set; }
        public bool EnableConcurrency { get; set; }
    }

    /// <summary>
    /// 获取所有预设配置
    /// </summary>
    public List<LaunchPreset> GetPresets()
    {
        return new List<LaunchPreset>
        {
            new LaunchPreset
            {
                Name = "低延迟",
                Type = PresetType.Performance,
                Description = "缩短垃圾回收停顿，适合较多模组",
                MinMemory = 2048,
                RecommendedMemory = 6144,
                JvmArgs = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=50",
                EnableG1GC = true,
                EnableOptimizations = true,
                EnableConcurrency = true
            },
            new LaunchPreset
            {
                Name = "兼容优先",
                Type = PresetType.Compatibility,
                Description = "最大化兼容性，适合老旧配置",
                MinMemory = 512,
                RecommendedMemory = 2048,
                JvmArgs = "",
                EnableG1GC = false,
                EnableOptimizations = false,
                EnableConcurrency = false
            },
            new LaunchPreset
            {
                Name = "平衡模式",
                Type = PresetType.Balanced,
                Description = "性能与兼容性平衡，适合大多数配置",
                MinMemory = 1536,
                RecommendedMemory = 4096,
                JvmArgs = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=100",
                EnableG1GC = true,
                EnableOptimizations = true,
                EnableConcurrency = false
            }
        };
    }

    /// <summary>
    /// 根据预设类型获取预设
    /// </summary>
    public LaunchPreset? GetPreset(PresetType type)
    {
        return GetPresets().Find(p => p.Type == type);
    }

    /// <summary>
    /// 根据系统配置推荐预设
    /// </summary>
    public LaunchPreset GetRecommendedPreset()
    {
        var totalMemory = GetSystemMemoryMB();

        if (totalMemory >= 8192) // 8GB+
        {
            return GetPreset(PresetType.Performance) ?? GetPreset(PresetType.Balanced)!;
        }
        else if (totalMemory >= 4096) // 4GB+
        {
            return GetPreset(PresetType.Balanced)!;
        }
        else // < 4GB
        {
            return GetPreset(PresetType.Compatibility) ?? GetPreset(PresetType.Balanced)!;
        }
    }

    public LaunchPreset MatchPreset(string? jvmArgs)
    {
        var normalized = NormalizeArgs(jvmArgs);
        return GetPresets().FirstOrDefault(preset => NormalizeArgs(preset.JvmArgs) == normalized)
            ?? GetPreset(PresetType.Balanced)!;
    }

    /// <summary>
    /// 获取系统内存大小（MB）
    /// </summary>
    public int GetSystemMemoryMB()
    {
        try
        {
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            return (int)(computerInfo.TotalPhysicalMemory / (1024 * 1024));
        }
        catch
        {
            return 2048; // 默认2GB
        }
    }

    public int GetRecommendedMemoryLimit()
    {
        var totalMemory = GetSystemMemoryMB();
        var reserveWindows = Math.Max(512, totalMemory - 2048);
        var percentageLimit = Math.Max(512, (int)(totalMemory * 0.8));
        return Math.Min(reserveWindows, percentageLimit);
    }

    /// <summary>
    /// 生成JVM参数
    /// </summary>
    public string BuildJvmArgs(LaunchPreset preset, string customArgs = "")
    {
        var args = new List<string>();

        // 添加预设参数
        if (!string.IsNullOrEmpty(preset.JvmArgs))
        {
            args.Add(preset.JvmArgs);
        }

        // 添加自定义参数
        if (!string.IsNullOrEmpty(customArgs))
        {
            args.Add(customArgs);
        }

        // 添加通用优化参数
        if (preset.EnableOptimizations)
        {
            args.Add("-Djava.net.preferIPv4Stack=true");
        }

        return string.Join(" ", args);
    }

    /// <summary>
    /// 验证内存设置
    /// </summary>
    public (bool valid, string? message) ValidateMemorySettings(int memory, LaunchPreset preset)
    {
        if (memory < preset.MinMemory)
        {
            return (false, $"内存设置过低，此预设至少需要 {preset.MinMemory}MB");
        }

        var recommendedLimit = GetRecommendedMemoryLimit();
        if (memory > recommendedLimit)
        {
            return (false, $"内存设置过高，建议不超过 {recommendedLimit}MB，以便为 Windows 保留内存");
        }

        return (true, null);
    }

    private static string NormalizeArgs(string? args) =>
        string.Join(" ", (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
