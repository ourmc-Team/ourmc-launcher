using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 游戏性能优化服务 - 基于PCL的FPS优化和性能调优
/// </summary>
public class PerformanceService
{
    private readonly string _optionsFilePath;
    private PerformanceSettings _currentSettings;

    public PerformanceService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var omlDir = Path.Combine(appData, "OMLLauncher");
        Directory.CreateDirectory(omlDir);

        _optionsFilePath = Path.Combine(omlDir, "performance_options.json");
        _currentSettings = LoadSettings();
    }

    /// <summary>
    /// 获取当前性能设置
    /// </summary>
    public PerformanceSettings GetCurrentSettings()
    {
        return _currentSettings;
    }

    /// <summary>
    /// 应用预设配置
    /// </summary>
    public void ApplyPreset(PerformancePreset preset)
    {
        _currentSettings = preset.Settings;
        SaveSettings();
    }

    /// <summary>
    /// 保存性能设置
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_currentSettings, options);
            File.WriteAllText(_optionsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存性能设置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载性能设置
    /// </summary>
    private PerformanceSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_optionsFilePath))
            {
                var json = File.ReadAllText(_optionsFilePath);
                var settings = JsonSerializer.Deserialize<PerformanceSettings>(json);
                if (settings != null)
                    return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载性能设置失败: {ex.Message}");
        }

        // 返回默认平衡设置
        return PerformancePreset.GetBalanced().Settings;
    }

    /// <summary>
    /// 生成Minecraft options.txt文件内容
    /// </summary>
    public string GenerateOptionsFile()
    {
        var s = _currentSettings;

        return $"# Minecraft性能优化配置 - 由OML启动器生成\n" +
               $"# 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
               $"invertedMouse:false\n" +
               $"mouseSensitivity:0.5\n" +
               $"fov:0.0\n" +
               $"difficulty:1\n" +
               $"graphics:{(s.FancyGraphics ? "fancy" : "fast")}\n" +
               $"renderDistance:{s.RenderDistance}\n" +
               $"ao:2\n" +
               $"anaglyph3d:{(s.Anaglyph ? "true" : "false")}\n" +
               $"advancedOpengl:{(s.AdvancedOpenGL ? "true" : "false")}\n" +
               $"framerateLimit:{(s.UnlimitedFPS ? 0 : s.TargetFPS)}\n" +
               $"limitFramerate:{(s.VSync ? "true" : "false")}\n" +
               $"smoothWorlds:{(s.SmoothWorld ? "true" : "false")}\n" +
               $"fboEnabled:true\n" +
               $"fboEnable:true\n" +
               $"renderer:minecraft:tweaked\n" +
               $"cloudsVisible:{(s.Clouds ? "true" : "false")}\n" +
               $"enableVsync:{(s.VSync ? "true" : "false")}\n" +
               $"enableVbo:true\n" +
               $"entityShadows:{(s.EntityShadows ? "true" : "false")}\n" +
               $"forceUnicodeFont:false\n" +
               $"discrete_mouse_scroll:false\n" +
               $"debugInfo:{(s.DebugInfo ? "true" : "false")}\n" +
               $"hideServerAddress:false\n" +
               $"advancedItemTooltips:true\n" +
               $"autoJump:false\n" +
               $"chatVis:" +
               $"showFPS:{(s.ShowFPS ? "true" : "false")}\n" +
               $"reducedDebugInfo:false\n" +
               $"touchscreen:false\n" +
               $"fullscreen:false\n" +
               $"bobView:true\n" +
               $"toggleCrouch:false\n" +
               $"toggleSprint:false\n" +
               $"mouseWheelSensitivity:1.0\n" +
               $"screenScale:0\n" +
               $"screenScaleShader:1\n" +
               $"gamma:0.0\n" +
               $"renderClouds:{(s.Clouds ? "true" : "false")}\n" +
               $"graphicsMode:{(s.FancyGraphics ? "fancy" : "fast")}\n" +
               $"particles:0\n" +
               $"heldItemTooltips:true\n" +
               $"chatVisibility:0\n" +
               $"mipmapLevels:4\n" +
               $"realmsNotifications:true\n" +
               $"attackIndicator:1\n" +
               $"narrator:0\n" +
               $"textBackgroundOpacity:64\n" +
               $"backgroundForChatOnly:true\n" +
               $"mainHand:right\n" +
               $"narratorToggle:true\n" +
               $"lang:zh_cn\n";
    }

    /// <summary>
    /// 应用性能设置到游戏目录
    /// </summary>
    public bool ApplyToGame(string gameDirectory)
    {
        try
        {
            var optionsFile = Path.Combine(gameDirectory, "options.txt");
            var optionsContent = GenerateOptionsFile();
            File.WriteAllText(optionsFile, optionsContent);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用性能设置失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取推荐的预设
    /// </summary>
    public PerformancePreset GetRecommendedPreset()
    {
        var systemMemory = GetSystemMemoryInGB();

        if (systemMemory < 4)
        {
            return PerformancePreset.GetLowEnd();
        }
        else if (systemMemory < 8)
        {
            return PerformancePreset.GetBalanced();
        }
        else if (systemMemory < 16)
        {
            return PerformancePreset.GetHighEnd();
        }
        else
        {
            return PerformancePreset.GetMaxPerformance();
        }
    }

    /// <summary>
    /// 获取系统内存大小（GB）
    /// </summary>
    private int GetSystemMemoryInGB()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return (int)(memStatus.ullTotalPhys / (1024 * 1024 * 1024));
                }
            }

            // 默认返回8GB
            return 8;
        }
        catch
        {
            return 8;
        }
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

    /// <summary>
    /// 获取性能评分（0-100）
    /// </summary>
    public int GetPerformanceScore()
    {
        var s = _currentSettings;
        var score = 50; // 基础分

        // 根据设置计算性能评分
        if (s.UnlimitedFPS) score += 10;
        if (s.TargetFPS >= 60) score += 10;
        if (!s.FancyGraphics) score += 15;
        if (s.FastRender) score += 10;
        if (s.FastMath) score += 5;
        if (!s.Clouds) score += 5;
        if (!s.EntityShadows) score += 5;
        if (!s.FancyLeaves) score += 5;
        if (!s.FancyWater) score += 5;
        if (s.RenderDistance <= 8) score += 10;
        else if (s.RenderDistance <= 12) score += 5;

        return Math.Min(100, Math.Max(0, score));
    }
}