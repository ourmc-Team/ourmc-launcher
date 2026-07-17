using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 模组服务 - 负责管理游戏模组
/// </summary>
public class ModService
{
    private readonly PathService _pathService;

    public ModService(PathService pathService)
    {
        _pathService = pathService;
    }

    /// <summary>
    /// 获取模组目录
    /// </summary>
    private string GetModsDir(string versionName)
    {
        return Path.Combine(_pathService.GameDir, "mods");
    }

    /// <summary>
    /// 获取禁用模组目录
    /// </summary>
    private string GetDisabledModsDir(string versionName)
    {
        return Path.Combine(_pathService.GameDir, "mods", versionName, "disabled");
    }

    /// <summary>
    /// 获取已安装的模组列表
    /// </summary>
    public List<ModInfo> GetInstalledMods(string versionName)
    {
        var mods = new List<ModInfo>();
        var modsDir = GetModsDir(versionName);

        if (!Directory.Exists(modsDir))
            return mods;

        // 扫描启用的模组
        foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
        {
            var mod = ParseModFile(file);
            mod.IsEnabled = true;
            mods.Add(mod);
        }

        // 扫描禁用的模组
        var disabledDir = GetDisabledModsDir(versionName);
        if (Directory.Exists(disabledDir))
        {
            foreach (var file in Directory.GetFiles(disabledDir, "*.jar"))
            {
                var mod = ParseModFile(file);
                mod.IsEnabled = false;
                mods.Add(mod);
            }
        }

        return mods.OrderBy(m => m.DisplayName).ToList();
    }

    /// <summary>
    /// 解析模组文件信息
    /// </summary>
    private ModInfo ParseModFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);

        var mod = new ModInfo
        {
            FileName = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FileSize = fileInfo.Length,
            Version = "未知",
            Description = "",
            Author = ""
        };

        try
        {
            // 尝试从 JAR 文件中读取 mcmod.info 或 fabric.mod.json
            using var archive = ZipFile.OpenRead(filePath);

            // 检查 Forge 模组 (mcmod.info)
            var mcmodEntry = archive.GetEntry("mcmod.info");
            if (mcmodEntry != null)
            {
                using var stream = mcmodEntry.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                // 简单解析（实际应该用 JSON 解析器）
                var jsonDoc = JsonDocument.Parse(content);
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var modInfo = jsonDoc.RootElement.EnumerateArray().FirstOrDefault();
                    if (modInfo.ValueKind != JsonValueKind.Undefined)
                    {
                        mod.DisplayName = modInfo.GetProperty("name").GetString() ?? mod.DisplayName;
                        mod.Version = modInfo.GetProperty("version").GetString() ?? "未知";
                        mod.Description = modInfo.GetProperty("description").GetString() ?? "";
                        mod.Author = modInfo.GetProperty("authorList").GetString() ?? "";
                    }
                }
            }

            // 检查 Fabric 模组 (fabric.mod.json)
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var stream = fabricEntry.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                mod.DisplayName = root.GetProperty("name").GetString() ?? mod.DisplayName;
                mod.Version = root.GetProperty("version").GetString() ?? "未知";
                mod.Description = root.GetProperty("description").GetString() ?? "";

                if (root.TryGetProperty("authors", out var authors))
                {
                    var authorList = authors.EnumerateArray()
                        .Select(a => a.GetProperty("name").GetString())
                        .Where(n => !string.IsNullOrEmpty(n));
                    mod.Author = string.Join(", ", authorList);
                }
            }

            // 尝试读取图标（支持多种路径和格式）
            string[] iconPaths = {
                "icon.png", "assets/icon.png", "logo.png", "assets/logo.png",
                "icon.jpg", "assets/icon.jpg", "logo.jpg", "assets/logo.jpg",
                "icon.jpeg", "assets/icon.jpeg", "logo.jpeg", "assets/logo.jpeg",
                "icon.gif", "assets/icon.gif", "logo.gif", "assets/logo.gif"
            };

            foreach (var iconPath in iconPaths)
            {
                try
                {
                    var iconEntry = archive.GetEntry(iconPath);
                    if (iconEntry != null)
                    {
                        using var stream = new MemoryStream();
                        using var iconStream = iconEntry.Open();
                        iconStream.CopyTo(stream);
                        var bytes = stream.ToArray();

                        // 只加载小于200KB的图标，避免加载大图片导致卡顿
                        if (bytes.Length > 0 && bytes.Length < 200 * 1024)
                        {
                            var base64 = Convert.ToBase64String(bytes);
                            var extension = Path.GetExtension(iconPath).TrimStart('.');
                            var mimeType = extension switch
                            {
                                "png" => "png",
                                "jpg" or "jpeg" => "jpeg",
                                "gif" => "gif",
                                _ => "png"
                            };
                            mod.Icon = $"data:image/{mimeType};base64,{base64}";
                            System.Diagnostics.Debug.WriteLine($"成功加载模组图标: {fileName} - {iconPath} ({bytes.Length} bytes)");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取模组图标失败 {fileName} - {iconPath}: {ex.Message}");
                }
            }

            // 如果没有找到图标，尝试从模组包中的其他常见位置查找
            if (string.IsNullOrEmpty(mod.Icon))
            {
                try
                {
                    // 搜索可能的图标文件
                    var possibleIcons = archive.Entries
                        .Where(e => e.FullName.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
                                   e.FullName.Contains("logo", StringComparison.OrdinalIgnoreCase))
                        .Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                   e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        .Take(5);

                    foreach (var iconEntry in possibleIcons)
                    {
                        if (iconEntry != null && iconEntry.Length < 200 * 1024)
                        {
                            try
                            {
                                using var stream = new MemoryStream();
                                using var iconStream = iconEntry.Open();
                                iconStream.CopyTo(stream);
                                var bytes = stream.ToArray();

                                if (bytes.Length > 0)
                                {
                                    var base64 = Convert.ToBase64String(bytes);
                                    var extension = Path.GetExtension(iconEntry.FullName).TrimStart('.');
                                    var mimeType = extension switch
                                    {
                                        "png" => "png",
                                        "jpg" or "jpeg" => "jpeg",
                                        "gif" => "gif",
                                        _ => "png"
                                    };
                                    mod.Icon = $"data:image/{mimeType};base64,{base64}";
                                    System.Diagnostics.Debug.WriteLine($"通过搜索找到模组图标: {fileName} - {iconEntry.FullName}");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"搜索模组图标失败 {fileName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析模组失败 {fileName}: {ex.Message}");
        }

        return mod;
    }

    /// <summary>
    /// 安装模组（复制 .jar 文件到 mods 目录）
    /// </summary>
    public async Task<bool> InstallModAsync(string versionName, string sourceJarPath)
    {
        try
        {
            if (!File.Exists(sourceJarPath))
                return false;

            var modsDir = GetModsDir(versionName);
            Directory.CreateDirectory(modsDir);

            var destPath = Path.Combine(modsDir, Path.GetFileName(sourceJarPath));

            // 如果文件已存在，询问是否覆盖
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            await Task.Run(() => File.Copy(sourceJarPath, destPath, true));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"安装模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 禁用模组（移动到 disabled 目录）
    /// </summary>
    public bool DisableMod(string versionName, string fileName)
    {
        try
        {
            var modsDir = GetModsDir(versionName);
            var sourcePath = Path.Combine(modsDir, fileName);

            if (!File.Exists(sourcePath))
                return false;

            var disabledDir = GetDisabledModsDir(versionName);
            Directory.CreateDirectory(disabledDir);

            var destPath = Path.Combine(disabledDir, fileName);

            File.Move(sourcePath, destPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"禁用模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 启用模组（移回 mods 目录）
    /// </summary>
    public bool EnableMod(string versionName, string fileName)
    {
        try
        {
            var disabledDir = GetDisabledModsDir(versionName);
            var sourcePath = Path.Combine(disabledDir, fileName);

            if (!File.Exists(sourcePath))
                return false;

            var modsDir = GetModsDir(versionName);
            Directory.CreateDirectory(modsDir);

            var destPath = Path.Combine(modsDir, fileName);

            File.Move(sourcePath, destPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启用模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 删除模组
    /// </summary>
    public bool DeleteMod(string versionName, string fileName, bool isEnabled)
    {
        try
        {
            var dir = isEnabled ? GetModsDir(versionName) : GetDisabledModsDir(versionName);
            var filePath = Path.Combine(dir, fileName);

            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"删除模组失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查版本是否支持模组
    /// </summary>
    public bool SupportsMods(string versionName)
    {
        // 检查是否为 Forge 或 Fabric 版本
        try
        {
            var versionDir = _pathService.GetVersionDir(versionName);
            var jsonPath = Path.Combine(versionDir, $"{versionName}.json");

            if (!File.Exists(jsonPath))
                return false;

            var json = File.ReadAllText(jsonPath);
            return json.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
                   json.Contains("fabric", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取模组文件大小格式化字符串
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int counter = 0;
        double number = bytes;

        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }
}
