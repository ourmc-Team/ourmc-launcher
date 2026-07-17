using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 资源管理服务 - 管理游戏资源（存档、资源包、截图等）
/// </summary>
public class ResourceService
{
    private readonly PathService _pathService;
    private readonly string _resourcesDir;

    public ResourceService(PathService pathService)
    {
        _pathService = pathService;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var omlDir = Path.Combine(appData, ".minecraft", "oml");
        _resourcesDir = Path.Combine(omlDir, "resources");
        Directory.CreateDirectory(_resourcesDir);
    }

    /// <summary>
    /// 获取所有世界/存档
    /// </summary>
    public List<WorldSave> GetWorldSaves(string versionId)
    {
        var saves = new List<WorldSave>();
        var savesDir = Path.Combine(_pathService.GameDir, "saves");

        if (!Directory.Exists(savesDir))
            return saves;

        try
        {
            foreach (var worldDir in Directory.GetDirectories(savesDir))
            {
                var levelDat = Path.Combine(worldDir, "level.dat");
                if (!File.Exists(levelDat)) continue;

                var worldName = Path.GetFileName(worldDir);
                var lastModified = Directory.GetLastWriteTime(worldDir);

                // 读取世界信息
                long size = 0;
                try
                {
                    size = Directory.GetFiles(worldDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                }
                catch { }

                saves.Add(new WorldSave
                {
                    Id = worldName,
                    Name = worldName,
                    LastPlayed = lastModified,
                    Size = size,
                    Path = worldDir,
                    VersionId = versionId
                });
            }

            return saves.OrderByDescending(s => s.LastPlayed).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取世界列表失败: {ex.Message}");
            return saves;
        }
    }

    /// <summary>
    /// 获取资源包列表
    /// </summary>
    public List<ResourcePack> GetResourcePacks()
    {
        var packs = new List<ResourcePack>();
        var resourcePacksDir = Path.Combine(_pathService.GameDir, "resourcepacks");

        if (!Directory.Exists(resourcePacksDir))
            return packs;

        try
        {
            foreach (var packFile in Directory.GetFiles(resourcePacksDir, "*.zip"))
            {
                var fileName = Path.GetFileNameWithoutExtension(packFile);
                var packInfo = GetResourcePackInfo(packFile);

                packs.Add(new ResourcePack
                {
                    Id = fileName,
                    Name = packInfo.Name ?? fileName,
                    Description = packInfo.Description ?? "",
                    Format = packInfo.Format ?? "unknown",
                    FilePath = packFile,
                    Enabled = !fileName.EndsWith("_disabled"),
                    Size = new FileInfo(packFile).Length
                });
            }

            return packs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取资源包列表失败: {ex.Message}");
            return packs;
        }
    }

    /// <summary>
    /// 获取截图列表
    /// </summary>
    public List<Screenshot> GetScreenshots()
    {
        var screenshots = new List<Screenshot>();
        var screenshotsDir = Path.Combine(_pathService.GameDir, "screenshots");

        if (!Directory.Exists(screenshotsDir))
            return screenshots;

        try
        {
            foreach (var screenshotFile in Directory.GetFiles(screenshotsDir, "*.png")
                .Concat(Directory.GetFiles(screenshotsDir, "*.jpg"))
                .Concat(Directory.GetFiles(screenshotsDir, "*.jpeg")))
            {
                var fileInfo = new FileInfo(screenshotFile);
                screenshots.Add(new Screenshot
                {
                    Id = fileInfo.Name,
                    Name = fileInfo.Name,
                    FilePath = screenshotFile,
                    Size = fileInfo.Length,
                    Created = fileInfo.CreationTime,
                    Format = fileInfo.Extension.TrimStart('.')
                });
            }

            return screenshots.OrderByDescending(s => s.Created).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取截图列表失败: {ex.Message}");
            return screenshots;
        }
    }

    /// <summary>
    /// 备份世界
    /// </summary>
    public bool BackupWorld(string worldPath)
    {
        try
        {
            if (!Directory.Exists(worldPath)) return false;

            var worldName = Path.GetFileName(worldPath);
            var backupDir = Path.Combine(_resourcesDir, "world_backups");
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir, $"{worldName}_{DateTime.Now:yyyyMMdd_HHmmss}");
            DirectoryCopy(worldPath, backupPath, true);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"备份世界失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 切换资源包启用状态
    /// </summary>
    public bool ToggleResourcePack(string packPath, bool enable)
    {
        try
        {
            if (enable)
            {
                // 启用：移除 _disabled 后缀
                if (packPath.EndsWith("_disabled.zip"))
                {
                    var newPath = packPath.Replace("_disabled.zip", ".zip");
                    File.Move(packPath, newPath);
                    return true;
                }
            }
            else
            {
                // 禁用：添加 _disabled 后缀
                var newPath = packPath.Replace(".zip", "_disabled.zip");
                File.Move(packPath, newPath);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"切换资源包状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取资源包信息
    /// </summary>
    private (string? Name, string? Description, string? Format) GetResourcePackInfo(string packPath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(packPath);
            var packMcmetaEntry = archive.GetEntry("pack.mcmeta");

            if (packMcmetaEntry != null)
            {
                using var stream = packMcmetaEntry.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var json = JsonNode.Parse(content);

                var pack = json?["pack"];
                if (pack != null)
                {
                    return (
                        pack["description"]?.ToString(),
                        pack?["pack_format"]?.ToString() ?? "",
                        pack?["pack_format"]?.ToString() ?? "unknown"
                    );
                }
            }

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// 目录复制辅助方法
    /// </summary>
    private void DirectoryCopy(string sourceDir, string targetDir, bool recursive)
    {
        var dir = Directory.CreateDirectory(targetDir);

        var files = Directory.GetFiles(sourceDir);
        foreach (var file in files)
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }

        if (recursive)
        {
            var dirs = Directory.GetDirectories(sourceDir);
            foreach (var dirPath in dirs)
            {
                DirectoryCopy(dirPath, Path.Combine(targetDir, Path.GetFileName(dirPath)), true);
            }
        }
    }
}

/// <summary>
/// 世界存档信息
/// </summary>
public class WorldSave
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastPlayed { get; set; }
    public long Size { get; set; }
    public string Path { get; set; } = "";
    public string VersionId { get; set; } = "";
}

/// <summary>
/// 资源包信息
/// </summary>
public class ResourcePack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Format { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool Enabled { get; set; }
    public long Size { get; set; }
}

/// <summary>
/// 截图信息
/// </summary>
public class Screenshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long Size { get; set; }
    public DateTime Created { get; set; }
    public string Format { get; set; } = "";
}
