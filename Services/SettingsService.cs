using System;
using System.IO;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

public class SettingsService
{
    private readonly string _settingsFile;
    private AppSettings _settings;

    public SettingsService()
        : this(GetDefaultAppDirectory())
    {
    }

    internal SettingsService(string appDir)
    {
        Directory.CreateDirectory(appDir);
        _settingsFile = Path.Combine(appDir, "settings.json");
        _settings = LoadSettings();
        MigrateSensitiveSettings();
    }

    public string GetJavaPath() => _settings.JavaPath;
    public int GetMaxMemory() => _settings.MaxMemory;
    public string GetGameDirectory() => _settings.GameDirectory;
    public string GetPlayerName() => _settings.PlayerName;
    public int GetBackgroundIndex() => _settings.BackgroundIndex;
    public bool GetFullscreen() => _settings.Fullscreen;
    public int GetWindowWidth() => _settings.WindowWidth;
    public int GetWindowHeight() => _settings.WindowHeight;
    public string GetCustomJvmArgs() => _settings.CustomJvmArgs;

    public void SetJavaPath(string path)
    {
        _settings.JavaPath = path;
        SaveSettings();
    }

    public void SetMaxMemory(int memory)
    {
        _settings.MaxMemory = memory;
        SaveSettings();
    }

    public void SetGameDirectory(string dir)
    {
        _settings.GameDirectory = dir;
        SaveSettings();
    }

    public void SetPlayerName(string name)
    {
        _settings.PlayerName = name;
        SaveSettings();
    }

    public void SetBackgroundIndex(int index)
    {
        _settings.BackgroundIndex = index;
        SaveSettings();
    }

    public void SetFullscreen(bool fullscreen)
    {
        _settings.Fullscreen = fullscreen;
        SaveSettings();
    }

    public void SetWindowSize(int width, int height)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        SaveSettings();
    }

    public void SetCustomJvmArgs(string args)
    {
        _settings.CustomJvmArgs = args;
        SaveSettings();
    }

    public string GetAIApiKey() => _settings.AIApiKey;

    public string GetAIProvider() => _settings.AIProvider;

    public void SetAIApiKey(string apiKey)
    {
        _settings.AIApiKey = apiKey;
        _settings.ProtectedAIApiKey = SecretProtectionService.Protect(apiKey);
        _settings.LegacyAIApiKey = null;
        SaveSettings();
    }

    public void SetAIProvider(string provider)
    {
        _settings.AIProvider = provider;
        SaveSettings();
    }

    public bool GetShowAIWelcome() => _settings.ShowAIWelcome;

    public void SetShowAIWelcome(bool showWelcome)
    {
        _settings.ShowAIWelcome = showWelcome;
        SaveSettings();
    }

    public AppSettings GetAllSettings() => _settings;

    public void Update(Action<AppSettings> update)
    {
        update(_settings);
        SaveSettings();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                System.Diagnostics.Debug.WriteLine($"从 {_settingsFile} 加载设置");
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"设置文件不存在，创建默认设置: {_settingsFile}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}，使用默认设置");
        }

        return new AppSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_settingsFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(_settingsFile, json);
            System.Diagnostics.Debug.WriteLine($"设置已保存到: {_settingsFile}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            throw;
        }
    }

    private void MigrateSensitiveSettings()
    {
        var shouldSave = false;

        if (SecretProtectionService.TryUnprotect(_settings.ProtectedAIApiKey, out var apiKey))
        {
            _settings.AIApiKey = apiKey;
        }

        if (string.IsNullOrEmpty(_settings.AIApiKey) &&
            !string.IsNullOrEmpty(_settings.LegacyAIApiKey))
        {
            _settings.AIApiKey = _settings.LegacyAIApiKey;
            _settings.ProtectedAIApiKey = SecretProtectionService.Protect(_settings.AIApiKey);
            shouldSave = true;
        }

        if (_settings.LegacyAIApiKey != null)
        {
            _settings.LegacyAIApiKey = null;
            shouldSave = true;
        }

        if (shouldSave)
        {
            SaveSettings();
        }
    }

    private static string GetDefaultAppDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "OMLLauncher");
    }
}
