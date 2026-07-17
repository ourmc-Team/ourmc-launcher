using System;

namespace ourmclauncher.Services;

/// <summary>
/// 主题服务 - 管理启动器主题
/// </summary>
public class ThemeService
{
    /// <summary>
    /// 主题类型
    /// </summary>
    public enum ThemeType
    {
        /// <summary>
        /// 深色主题
        /// </summary>
        Dark = 0,

        /// <summary>
        /// 浅色主题
        /// </summary>
        Light = 1,

        /// <summary>
        /// 自动（跟随系统）
        /// </summary>
        Auto = 2
    }

    /// <summary>
    /// 当前主题
    /// </summary>
    public ThemeType CurrentTheme { get; set; } = ThemeType.Dark;

    /// <summary>
    /// 主题变更事件
    /// </summary>
    public event Action<ThemeType>? ThemeChanged;

    /// <summary>
    /// 设置主题
    /// </summary>
    public void SetTheme(ThemeType theme)
    {
        CurrentTheme = theme;
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>
    /// 获取有效的主题类型（处理自动模式）
    /// </summary>
    public ThemeType GetEffectiveTheme()
    {
        if (CurrentTheme == ThemeType.Auto)
        {
            // 检测系统主题
            return IsSystemLightTheme() ? ThemeType.Light : ThemeType.Dark;
        }
        return CurrentTheme;
    }

    /// <summary>
    /// 检测系统是否为浅色主题
    /// </summary>
    private bool IsSystemLightTheme()
    {
        try
        {
            // 简单的系统主题检测
            var appsUseLightTheme = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);

            return appsUseLightTheme?.ToString() == "1";
        }
        catch
        {
            return true; // 默认浅色
        }
    }

    /// <summary>
    /// 获取主题CSS类名
    /// </summary>
    public string GetThemeClass()
    {
        return GetEffectiveTheme() switch
        {
            ThemeType.Light => "theme-light",
            ThemeType.Dark => "theme-dark",
            _ => "theme-dark"
        };
    }

    /// <summary>
    /// 切换主题
    /// </summary>
    public void ToggleTheme()
    {
        var newTheme = GetEffectiveTheme() == ThemeType.Dark ? ThemeType.Light : ThemeType.Dark;
        SetTheme(newTheme);
    }
}
