namespace ourmclauncher.Models;

/// <summary>
/// 游戏性能优化设置
/// </summary>
public class PerformanceSettings
{
    // FPS优化设置
    public bool UnlimitedFPS { get; set; } = false;
    public int TargetFPS { get; set; } = 60;
    public bool VSync { get; set; } = true;

    // 渲染优化
    public bool SmoothWorld { get; set; } = true;
    public bool FancyGraphics { get; set; } = true;
    public int RenderDistance { get; set; } = 12; // 渲染距离（ chunks）
    public bool AdvancedOpenGL { get; set; } = false;

    // 性能优化
    public bool FastRender { get; set; } = false;
    public bool FastMath { get; set; } = false;
    public bool Anaglyph { get; set; } = false;
    public bool AnisotropicFiltering { get; set; } = true;

    // 环境设置
    public bool Clouds { get; set; } = true;
    public bool EntityShadows { get; set; } = true;
    public bool FancyLeaves { get; set; } = true;
    public bool FancyWater { get; set; } = true;

    // 高级设置
    public bool EnableProfiling { get; set; } = false;
    public bool DebugInfo { get; set; } = false;
    public bool ShowFPS { get; set; } = false;
}

/// <summary>
/// 预设优化配置
/// </summary>
public class PerformancePreset
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public PerformanceSettings Settings { get; set; } = new();

    public static PerformancePreset GetLowEnd()
    {
        return new PerformancePreset
        {
            Name = "低配模式",
            Description = "适合配置较低的电脑，优先保证流畅度",
            Settings = new PerformanceSettings
            {
                TargetFPS = 30,
                RenderDistance = 6,
                FancyGraphics = false,
                SmoothWorld = false,
                FastRender = true,
                FastMath = true,
                Clouds = false,
                EntityShadows = false,
                FancyLeaves = false,
                FancyWater = false,
                AdvancedOpenGL = true
            }
        };
    }

    public static PerformancePreset GetBalanced()
    {
        return new PerformancePreset
        {
            Name = "平衡模式",
            Description = "在画质和性能之间取得平衡",
            Settings = new PerformanceSettings
            {
                TargetFPS = 60,
                RenderDistance = 10,
                FancyGraphics = true,
                SmoothWorld = true,
                FastRender = false,
                FastMath = false,
                Clouds = true,
                EntityShadows = true,
                FancyLeaves = true,
                FancyWater = true,
                AdvancedOpenGL = false
            }
        };
    }

    public static PerformancePreset GetHighEnd()
    {
        return new PerformancePreset
        {
            Name = "高配模式",
            Description = "适合配置较高的电脑，提供最佳画质",
            Settings = new PerformanceSettings
            {
                UnlimitedFPS = true,
                TargetFPS = 120,
                RenderDistance = 16,
                FancyGraphics = true,
                SmoothWorld = true,
                VSync = false,
                Clouds = true,
                EntityShadows = true,
                FancyLeaves = true,
                FancyWater = true,
                AnisotropicFiltering = true,
                AdvancedOpenGL = true
            }
        };
    }

    public static PerformancePreset GetMaxPerformance()
    {
        return new PerformancePreset
        {
            Name = "极致性能",
            Description = "最大化FPS，适合PVP等需要高帧率的场景",
            Settings = new PerformanceSettings
            {
                UnlimitedFPS = true,
                TargetFPS = 0,
                RenderDistance = 8,
                FancyGraphics = false,
                SmoothWorld = false,
                VSync = false,
                FastRender = true,
                FastMath = true,
                Clouds = false,
                EntityShadows = false,
                FancyLeaves = false,
                FancyWater = false,
                AdvancedOpenGL = true,
                ShowFPS = true
            }
        };
    }
}