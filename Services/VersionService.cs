using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 版本服务 - 负责扫描和管理已安装的 Minecraft 版本
/// </summary>
public class VersionService
{
    private readonly PathService _pathService;
    private readonly string _versionsFile;
    private List<GameVersion> _versions = new();

    public VersionService(PathService pathService)
    {
        _pathService = pathService;
        _versionsFile = Path.Combine(_pathService.AppDataDir, "oml_versions.json");
        LoadVersions();
    }

    public List<GameVersion> GetVersions() => _versions;

    public void AddVersion(GameVersion version)
    {
        if (_versions.Exists(v => v.Name == version.Name))
            return;

        _versions.Add(version);
        SaveVersions();
    }

    public void RemoveVersion(string versionName)
    {
        _versions.RemoveAll(v => v.Name == versionName);
        SaveVersions();
    }

    public bool VersionExists(string versionName)
    {
        return _versions.Exists(v => v.Name == versionName);
    }

    public void ScanInstalledVersions()
    {
        var versionsDir = Path.Combine(_pathService.GameDir, "versions");
        if (!Directory.Exists(versionsDir))
            return;

        _versions.Clear();
        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var name = Path.GetFileName(dir);
            var jarPath = Path.Combine(dir, $"{name}.jar");
            var jsonPath = Path.Combine(dir, $"{name}.json");

            if (File.Exists(jsonPath))
            {
                var type = DetermineVersionType(name, jsonPath);
                _versions.Add(new GameVersion { Id = name, Name = name, Type = type });
            }
        }

        SaveVersions();
    }

    private string DetermineVersionType(string name, string jsonPath)
    {
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("inheritsFrom", out var inheritsFrom))
            {
                var parent = inheritsFrom.GetString() ?? "";
                return parent switch
                {
                    _ when name.Contains("forge", StringComparison.OrdinalIgnoreCase) => "Forge",
                    _ when name.Contains("fabric", StringComparison.OrdinalIgnoreCase) => "Fabric",
                    _ when name.Contains("quilt", StringComparison.OrdinalIgnoreCase) => "Quilt",
                    _ when name.Contains("optifine", StringComparison.OrdinalIgnoreCase) => "OptiFine",
                    _ => $"模组 ({parent})"
                };
            }

            return "正式版";
        }
        catch
        {
            return "正式版";
        }
    }

    private void LoadVersions()
    {
        try
        {
            if (File.Exists(_versionsFile))
            {
                var json = File.ReadAllText(_versionsFile);
                _versions = JsonSerializer.Deserialize<List<GameVersion>>(json) ?? new();
                var repairedIds = false;
                foreach (var version in _versions.Where(version => string.IsNullOrWhiteSpace(version.Id)))
                {
                    version.Id = version.Name;
                    repairedIds = true;
                }

                if (repairedIds)
                {
                    SaveVersions();
                }
            }
            else
            {
                ScanInstalledVersions();
            }
        }
        catch
        {
            _versions = new();
        }
    }

    private void SaveVersions()
    {
        try
        {
            var json = JsonSerializer.Serialize(_versions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_versionsFile, json);
        }
        catch (Exception ex)
        {
            // 静默失败，避免影响用户体验
            System.Diagnostics.Debug.WriteLine($"保存版本列表失败: {ex.Message}");
        }
    }
}
