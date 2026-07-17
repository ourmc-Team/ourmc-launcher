using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 实例管理服务 - 管理多个游戏实例
/// 参考了MultiMC的实例管理架构
/// </summary>
public class InstanceService
{
    private readonly string _instancesDir;
    private readonly string _instancesFile;
    private List<GameInstance> _instances = new();

    public InstanceService(SettingsService settingsService)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var omlDir = Path.Combine(appData, ".minecraft", "oml");
        Directory.CreateDirectory(omlDir);

        _instancesDir = Path.Combine(omlDir, "instances");
        Directory.CreateDirectory(_instancesDir);

        _instancesFile = Path.Combine(omlDir, "instances.json");
        LoadInstances();
    }

    /// <summary>
    /// 获取所有实例
    /// </summary>
    public List<GameInstance> GetInstances()
    {
        return new List<GameInstance>(_instances);
    }

    /// <summary>
    /// 根据ID获取实例
    /// </summary>
    public GameInstance? GetInstance(string instanceId)
    {
        return _instances.FirstOrDefault(i => i.Id == instanceId);
    }

    /// <summary>
    /// 创建新实例
    /// </summary>
    public GameInstance CreateInstance(string name, string versionId, Models.InstanceType type = Models.InstanceType.Custom)
    {
        var instance = new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            VersionId = versionId,
            Type = type,
            CreatedAt = DateTime.Now,
            LastPlayedAt = DateTime.MinValue,
            TotalPlayTime = 0
        };

        // 创建实例目录结构
        var instanceDir = GetInstanceDir(instance.Id);
        Directory.CreateDirectory(instanceDir);
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "mods"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "saves"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "screenshots"));

        // 创建实例配置文件
        var configPath = Path.Combine(instanceDir, "instance.json");
        var config = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, config);

        _instances.Add(instance);
        SaveInstances();

        return instance;
    }

    /// <summary>
    /// 更新实例
    /// </summary>
    public void UpdateInstance(GameInstance instance)
    {
        var index = _instances.FindIndex(i => i.Id == instance.Id);
        if (index >= 0)
        {
            _instances[index] = instance;

            // 更新配置文件
            var configPath = Path.Combine(GetInstanceDir(instance.Id), "instance.json");
            var config = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, config);

            SaveInstances();
        }
    }

    /// <summary>
    /// 删除实例
    /// </summary>
    public bool DeleteInstance(string instanceId)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return false;

        try
        {
            var instanceDir = GetInstanceDir(instanceId);
            if (Directory.Exists(instanceDir))
            {
                Directory.Delete(instanceDir, recursive: true);
            }

            _instances.Remove(instance);
            SaveInstances();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除实例失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 导出实例
    /// </summary>
    public string ExportInstance(string instanceId, string exportPath)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return "";

        try
        {
            var instanceDir = GetInstanceDir(instanceId);
            var zipPath = Path.Combine(exportPath, $"{instance.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            ZipFile.CreateFromDirectory(instanceDir, zipPath);

            return zipPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出实例失败: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 导入实例
    /// </summary>
    public GameInstance? ImportInstance(string zipPath)
    {
        try
        {
            // 临时解压目录
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // 查找实例配置文件
            var configPath = Path.Combine(tempDir, "instance.json");
            if (!File.Exists(configPath))
            {
                Directory.Delete(tempDir, recursive: true);
                return null;
            }

            var config = File.ReadAllText(configPath);
            var instance = JsonSerializer.Deserialize<GameInstance>(config);
            if (instance == null) return null;

            // 生成新的ID避免冲突
            instance.Id = Guid.NewGuid().ToString("N");

            // 移动到实例目录
            var instanceDir = GetInstanceDir(instance.Id);
            if (Directory.Exists(instanceDir))
            {
                Directory.Delete(instanceDir, recursive: true);
            }
            Directory.Move(tempDir, instanceDir);

            _instances.Add(instance);
            SaveInstances();

            return instance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入实例失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 备份实例
    /// </summary>
    public bool BackupInstance(string instanceId)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return false;

        try
        {
            var backupDir = Path.Combine(_instancesDir, "backups");
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir, $"{instance.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            if (File.Exists(backupPath))
                File.Delete(backupPath);

            var instanceDir = GetInstanceDir(instanceId);
            ZipFile.CreateFromDirectory(instanceDir, backupPath);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"备份实例失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取实例的Minecraft目录
    /// </summary>
    public string GetInstanceMinecraftDir(string instanceId)
    {
        return Path.Combine(GetInstanceDir(instanceId), "minecraft");
    }

    /// <summary>
    /// 获取实例的Mods目录
    /// </summary>
    public string GetInstanceModsDir(string instanceId)
    {
        return Path.Combine(GetInstanceDir(instanceId), "mods");
    }

    /// <summary>
    /// 获取实例目录
    /// </summary>
    public string GetInstanceDir(string instanceId)
    {
        return Path.Combine(_instancesDir, instanceId);
    }

    /// <summary>
    /// 更新最后游玩时间
    /// </summary>
    public void UpdateLastPlayedTime(string instanceId)
    {
        var instance = GetInstance(instanceId);
        if (instance != null)
        {
            instance.LastPlayedAt = DateTime.Now;
            UpdateInstance(instance);
        }
    }

    /// <summary>
    /// 增加游玩时间
    /// </summary>
    public void AddPlayTime(string instanceId, long seconds)
    {
        var instance = GetInstance(instanceId);
        if (instance != null)
        {
            instance.TotalPlayTime += seconds;
            UpdateInstance(instance);
        }
    }

    private void LoadInstances()
    {
        try
        {
            if (File.Exists(_instancesFile))
            {
                var json = File.ReadAllText(_instancesFile);
                _instances = JsonSerializer.Deserialize<List<GameInstance>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载实例列表失败: {ex.Message}");
            _instances = new();
        }
    }

    private void SaveInstances()
    {
        try
        {
            var json = JsonSerializer.Serialize(_instances, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_instancesFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存实例列表失败: {ex.Message}");
        }
    }
}
