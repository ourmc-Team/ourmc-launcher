using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 模组管理服务 - 基于PCL/PCL2的模组管理功能
/// 支持模组加载、配置、版本兼容性检查等
/// </summary>
public class ModManagementService
{
    private readonly PathService _pathService;
    private readonly Dictionary<string, ModMetadata> _modCache = new();

    public ModManagementService(PathService pathService)
    {
        _pathService = pathService;
    }

    /// <summary>
    /// 获取模组目录
    /// </summary>
    public string GetModsDirectory(string versionId)
    {
        var gameDir = _pathService.GameDir;
        return Path.Combine(gameDir, "mods");
    }

    /// <summary>
    /// 扫描模组
    /// </summary>
    public async Task<List<ModMetadata>> ScanModsAsync(string versionId)
    {
        var mods = new List<ModMetadata>();
        var modsDir = GetModsDirectory(versionId);

        if (!Directory.Exists(modsDir))
            return mods;

        try
        {
            var modFiles = Directory.GetFiles(modsDir, "*.jar");

            foreach (var modFile in modFiles)
            {
                var modInfo = await ExtractModInfoAsync(modFile);
                if (modInfo != null)
                {
                    mods.Add(modInfo);
                }
            }

            // 按名称排序
            mods = mods.OrderBy(m => m.Name).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"扫描模组失败: {ex.Message}");
        }

        return mods;
    }

    /// <summary>
    /// 提取模组信息
    /// </summary>
    private async Task<ModMetadata?> ExtractModInfoAsync(string modFilePath)
    {
        try
        {
            var modInfo = new ModMetadata
            {
                FilePath = modFilePath,
                FileName = Path.GetFileName(modFilePath),
                FileSize = new FileInfo(modFilePath).Length,
                Enabled = !modFilePath.EndsWith(".disabled")
            };

            // 打开JAR文件读取模组信息
            using var archive = ZipFile.OpenRead(modFilePath);

            // 读取mcmod.info
            var mcmodEntry = archive.GetEntry("mcmod.info");
            if (mcmodEntry != null)
            {
                using var stream = mcmodEntry.Open();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();

                modInfo.ModLoader = "Forge";
                ParseMcmodInfo(content, modInfo);
            }
            else
            {
                // 尝试读取fabric.mod.json
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var stream = fabricEntry.Open();
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();

                    modInfo.ModLoader = "Fabric";
                    ParseFabricModInfo(content, modInfo);
                }
                else
                {
                    // 默认为未知模组加载器
                    modInfo.ModLoader = "Unknown";
                    modInfo.Name = Path.GetFileNameWithoutExtension(modFilePath);
                    modInfo.Version = "未知";
                    modInfo.Description = "无法识别模组信息";
                }
            }

            // 读取logo
            var logoEntry = archive.GetEntry("logo.png");
            if (logoEntry != null && logoEntry.Length > 0)
            {
                modInfo.HasLogo = true;
            }

            return modInfo;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"提取模组信息失败 {modFilePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析mcmod.info
    /// </summary>
    private void ParseMcmodInfo(string content, ModMetadata modInfo)
    {
        try
        {
            // 简化的mcmod.info解析
            var lines = content.Split('\n');
            string? currentSection = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    continue;
                }

                if (currentSection == null) continue;

                if (trimmedLine.Contains(":"))
                {
                    var parts = trimmedLine.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim().Trim('"');

                        switch (key)
                        {
                            case "name":
                                modInfo.Name = value;
                                break;
                            case "version":
                                modInfo.Version = value;
                                break;
                            case "description":
                                modInfo.Description = value;
                                break;
                            case "authorList":
                                modInfo.Authors = value.Replace("{", "").Replace("}", "").Replace(",", ", ");
                                break;
                            case "url":
                                modInfo.Url = value;
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"解析mcmod.info失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析Fabric模组信息
    /// </summary>
    private void ParseFabricModInfo(string content, ModMetadata modInfo)
    {
        try
        {
            var json = JsonDocument.Parse(content);
            var root = json.RootElement;

            if (root.TryGetProperty("id", out var idProp))
            {
                modInfo.Name = idProp.GetString() ?? "";
            }

            if (root.TryGetProperty("version", out var versionProp))
            {
                modInfo.Version = versionProp.GetString() ?? "";
            }

            if (root.TryGetProperty("description", out var descProp))
            {
                modInfo.Description = descProp.GetString() ?? "";
            }

            if (root.TryGetProperty("authors", out var authorsProp))
            {
                var authors = new List<string>();
                foreach (var author in authorsProp.EnumerateArray())
                {
                    var name = author.GetString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                        authors.Add(name);
                }
                modInfo.Authors = string.Join(", ", authors);
            }

            if (root.TryGetProperty("contact", out var contactProp))
            {
                var contact = contactProp.GetString() ?? "";
                if (contact.StartsWith("http"))
                {
                    modInfo.Url = contact;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"解析Fabric模组信息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 启用/禁用模组
    /// </summary>
    public bool ToggleMod(string versionId, string modFileName, bool enable)
    {
        try
        {
            var modsDir = GetModsDirectory(versionId);
            var sourceFile = Path.Combine(modsDir, modFileName);
            var targetFile = enable ? sourceFile.Replace(".disabled", "") : sourceFile + ".disabled";

            if (File.Exists(sourceFile))
            {
                File.Move(sourceFile, targetFile);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"切换模组状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 删除模组
    /// </summary>
    public bool DeleteMod(string versionId, string modFileName)
    {
        try
        {
            var modsDir = GetModsDirectory(versionId);
            var modFile = Path.Combine(modsDir, modFileName);

            if (File.Exists(modFile))
            {
                File.Delete(modFile);
                return true;
            }

            // 也检查禁用的模组
            var disabledFile = modFile + ".disabled";
            if (File.Exists(disabledFile))
            {
                File.Delete(disabledFile);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 添加模组
    /// </summary>
    public async Task<bool> AddModAsync(string versionId, string sourceFilePath)
    {
        try
        {
            var modsDir = GetModsDirectory(versionId);
            Directory.CreateDirectory(modsDir);

            var targetFile = Path.Combine(modsDir, Path.GetFileName(sourceFilePath));

            // 检查模组兼容性
            var isCompatible = await CheckModCompatibilityAsync(versionId, sourceFilePath);
            if (!isCompatible)
            {
                return false;
            }

            File.Copy(sourceFilePath, targetFile, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"添加模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查模组兼容性
    /// </summary>
    private async Task<bool> CheckModCompatibilityAsync(string versionId, string modFilePath)
    {
        try
        {
            // 获取游戏版本信息
            var versionMetadata = GetVersionMetadata(versionId);
            if (versionMetadata == null)
                return true; // 无法检查版本，默认允许

            // 读取模组信息
            var modInfo = await ExtractModInfoAsync(modFilePath);
            if (modInfo == null)
                return true;

            // 检查模组加载器兼容性
            if (!string.IsNullOrEmpty(modInfo.ModLoader) && modInfo.ModLoader != "Unknown")
            {
                // 这里可以添加更复杂的兼容性检查
                // 暂时简单检查
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查模组兼容性失败: {ex.Message}");
            return true; // 默认允许
        }
    }

    /// <summary>
    /// 获取版本元数据
    /// </summary>
    private VersionMetadata? GetVersionMetadata(string versionId)
    {
        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            var jsonFile = Path.Combine(versionDir, $"{versionId}.json");

            if (!File.Exists(jsonFile))
                return null;

            var json = File.ReadAllText(jsonFile);
            var versionData = JsonNode.Parse(json);

            return new VersionMetadata
            {
                Id = versionId,
                Type = versionData?["type"]?.ToString() ?? "",
                ReleaseTime = versionData?["releaseTime"]?.ToString() ?? "",
                JavaVersion = versionData?["javaVersion"]?.ToString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取模组加载器类型
    /// </summary>
    public string GetModLoaderType(string versionId)
    {
        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            var versionFile = Path.Combine(versionDir, $"{versionId}.json");

            if (!File.Exists(versionFile))
                return "Unknown";

            var json = File.ReadAllText(versionFile);
            var versionData = JsonNode.Parse(json);

            // 检查inheritsFrom字段来判断是否是Forge/Fabric版本
            var inheritsFrom = versionData?["inheritsFrom"]?.ToString();
            if (!string.IsNullOrEmpty(inheritsFrom))
            {
                if (inheritsFrom.Contains("forge", StringComparison.OrdinalIgnoreCase))
                    return "Forge";
                else if (inheritsFrom.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                    return "Fabric";
            }

            return "Vanilla";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 导出模组配置
    /// </summary>
    public async Task<string> ExportModListAsync(string versionId)
    {
        var mods = await ScanModsAsync(versionId);
        var modLoader = GetModLoaderType(versionId);

        var exportData = new List<string>
        {
            $"# 模组列表导出",
            $"# 游戏版本: {versionId}",
            $"# 模组加载器: {modLoader}",
            $"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"",
            $"# 启用的模组 ({mods.Count(m => m.Enabled)})"
        };

        foreach (var mod in mods.Where(m => m.Enabled))
        {
            exportData.Add($"- {mod.Name} v{mod.Version}");
            if (!string.IsNullOrEmpty(mod.Authors))
                exportData.Add($"  作者: {mod.Authors}");
            if (!string.IsNullOrEmpty(mod.Description))
                exportData.Add($"  描述: {mod.Description}");
            exportData.Add("");
        }

        var disabledMods = mods.Where(m => !m.Enabled).ToList();
        if (disabledMods.Count > 0)
        {
            exportData.Add("");
            exportData.Add("# 禁用的模组");
            foreach (var mod in disabledMods)
            {
                exportData.Add($"- {mod.Name} v{mod.Version}");
            }
        }

        return string.Join("\n", exportData);
    }
}

/// <summary>
/// 模组元数据
/// </summary>
public class ModMetadata
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Authors { get; set; } = "";
    public string Url { get; set; } = "";
    public string ModLoader { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long FileSize { get; set; }
    public bool HasLogo { get; set; }
}
