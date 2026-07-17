using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 版本管理服务 - 基于PCL/PCL2的版本管理功能
/// 支持版本隔离、依赖管理、增量更新等
/// </summary>
public class VersionManagementService
{
    private readonly PathService _pathService;
    private readonly Dictionary<string, VersionMetadata> _versionCache = new();

    public VersionManagementService(PathService pathService)
    {
        _pathService = pathService;
        InitializeVersionCache();
    }

    /// <summary>
    /// 初始化版本缓存
    /// </summary>
    private void InitializeVersionCache()
    {
        try
        {
            var versionsDir = _pathService.GetVersionsDir();
            if (!Directory.Exists(versionsDir))
                return;

            foreach (var versionDir in Directory.GetDirectories(versionsDir))
            {
                var versionId = Path.GetFileName(versionDir);
                var jsonFile = Path.Combine(versionDir, $"{versionId}.json");

                if (File.Exists(jsonFile))
                {
                    try
                    {
                        var json = File.ReadAllText(jsonFile);
                        var versionData = JsonNode.Parse(json);

                        _versionCache[versionId] = new VersionMetadata
                        {
                            Id = versionId,
                            Type = versionData?["type"]?.ToString() ?? "",
                            ReleaseTime = versionData?["releaseTime"]?.ToString() ?? "",
                            MainClass = versionData?["mainClass"]?.ToString() ?? "",
                            JavaVersion = versionData?["javaVersion"]?.ToString() ?? "",
                            Assets = versionData?["assetIndex"]?["id"]?.ToString() ?? "",
                            Libraries = ExtractLibrariesInfo(versionData?["libraries"] as JsonArray)
                        };
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"解析版本信息失败 {versionId}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"初始化版本缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取版本元数据
    /// </summary>
    public VersionMetadata? GetVersionMetadata(string versionId)
    {
        return _versionCache.TryGetValue(versionId, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// 获取所有已安装版本
    /// </summary>
    public List<VersionMetadata> GetInstalledVersions()
    {
        return _versionCache.Values.OrderBy(v => v.ReleaseTime).ToList();
    }

    /// <summary>
    /// 检查版本完整性
    /// </summary>
    public async Task<VersionIntegrity> CheckVersionIntegrityAsync(string versionId)
    {
        var result = new VersionIntegrity { VersionId = versionId };

        try
        {
            var metadata = GetVersionMetadata(versionId);
            if (metadata == null)
            {
                result.IsValid = false;
                result.MissingFiles.Add("版本元数据");
                return result;
            }

            var versionDir = _pathService.GetVersionDir(versionId);

            // 检查客户端JAR
            var jarPath = _pathService.GetVersionJarPath(versionId);
            if (!File.Exists(jarPath))
            {
                result.MissingFiles.Add("客户端JAR");
                result.IsValid = false;
            }

            // 检查版本JSON
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
            if (!File.Exists(jsonPath))
            {
                result.MissingFiles.Add("版本JSON");
                result.IsValid = false;
            }

            // 检查依赖库
            var missingLibs = await CheckLibrariesIntegrityAsync(versionId);
            result.MissingLibraries.AddRange(missingLibs);

            if (result.MissingLibraries.Count > 0)
            {
                result.IsValid = false;
            }

            // 检查native库
            var missingNatives = CheckNativesIntegrity(versionId);
            result.MissingNatives.AddRange(missingNatives);

            if (result.MissingNatives.Count > 0)
            {
                result.IsValid = false;
            }

            // 检查资源索引
            if (!string.IsNullOrEmpty(metadata.Assets))
            {
                var assetsDir = _pathService.GetAssetIndexesDir();
                var assetIndexPath = Path.Combine(assetsDir, $"{metadata.Assets}.json");

                if (!File.Exists(assetIndexPath))
                {
                    result.MissingFiles.Add($"资源索引 {metadata.Assets}");
                    result.IsValid = false;
                }
            }

            // 计算版本大小
            result.TotalSize = CalculateVersionSize(versionId);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查版本完整性失败 {versionId}: {ex.Message}");
            result.IsValid = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 检查依赖库完整性
    /// </summary>
    private async Task<List<string>> CheckLibrariesIntegrityAsync(string versionId)
    {
        var missingLibs = new List<string>();

        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");

            if (!File.Exists(jsonPath))
                return missingLibs;

            var json = await File.ReadAllTextAsync(jsonPath);
            var versionData = JsonNode.Parse(json);
            var libraries = versionData?["libraries"] as JsonArray;

            if (libraries == null)
                return missingLibs;

            var libDir = _pathService.GetLibrariesDir();

            foreach (var lib in libraries)
            {
                // 检查规则
                if (lib?["rules"] != null)
                {
                    var allowed = CheckLibraryRules(lib?["rules"] as JsonArray);
                    if (!allowed) continue;
                }

                var artifact = lib?["downloads"]?["artifact"];
                if (artifact != null)
                {
                    var libPath = artifact["path"]?.ToString();
                    if (!string.IsNullOrEmpty(libPath))
                    {
                        var fullPath = Path.Combine(libDir, libPath);
                        if (!File.Exists(fullPath))
                        {
                            missingLibs.Add(libPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查依赖库失败: {ex.Message}");
        }

        return missingLibs;
    }

    /// <summary>
    /// 检查native库完整性
    /// </summary>
    private List<string> CheckNativesIntegrity(string versionId)
    {
        var missingNatives = new List<string>();

        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");

            if (!File.Exists(jsonPath))
                return missingNatives;

            var json = File.ReadAllText(jsonPath);
            var versionData = JsonNode.Parse(json);
            var libraries = versionData?["libraries"] as JsonArray;

            if (libraries == null)
                return missingNatives;

            var nativesDir = _pathService.GetNativesDir(versionId);

            foreach (var lib in libraries)
            {
                var classifiers = lib?["downloads"]?["classifiers"];
                if (classifiers == null) continue;

                JsonNode? nativeArtifact = null;
                if (classifiers["natives-windows"] != null)
                    nativeArtifact = classifiers["natives-windows"];
                else if (classifiers["natives-windows-lgpl"] != null)
                    nativeArtifact = classifiers["natives-windows-lgpl"];

                if (nativeArtifact != null)
                {
                    // 检查native库文件是否存在
                    var expectedFiles = GetExpectedNativeFiles(nativeArtifact);
                    foreach (var file in expectedFiles)
                    {
                        var filePath = Path.Combine(nativesDir, file);
                        if (!File.Exists(filePath))
                        {
                            missingNatives.Add(file);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查native库失败: {ex.Message}");
        }

        return missingNatives;
    }

    /// <summary>
    /// 获取期望的native库文件列表
    /// </summary>
    private List<string> GetExpectedNativeFiles(JsonNode nativeArtifact)
    {
        // 简化实现，实际需要从JAR中提取文件列表
        return new List<string>
        {
            "openal32.dll",
            "lwjgl.dll",
            "lwjgl-opengl.dll",
            "jinput-dx8.dll",
            "jinput-raw.dll"
        };
    }

    /// <summary>
    /// 检查库规则
    /// </summary>
    private bool CheckLibraryRules(JsonArray? rules)
    {
        if (rules == null) return true;

        var allowed = true;
        var os = Environment.OSVersion.Platform.ToString().ToLower();

        foreach (var rule in rules)
        {
            var action = rule?["action"]?.ToString();
            var osName = rule?["os"]?["name"]?.ToString();

            if (action == "disallow")
            {
                if (osName != null && os.Contains(osName.ToLower()))
                    allowed = false;
            }
            else if (action == "allow")
            {
                if (osName != null && os.Contains(osName.ToLower()))
                    allowed = true;
            }
        }

        return allowed;
    }

    /// <summary>
    /// 计算版本大小
    /// </summary>
    private long CalculateVersionSize(string versionId)
    {
        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            if (!Directory.Exists(versionDir))
                return 0;

            long totalSize = 0;

            // 计算JAR文件大小
            var jarPath = _pathService.GetVersionJarPath(versionId);
            if (File.Exists(jarPath))
                totalSize += new FileInfo(jarPath).Length;

            // 计算依赖库大小
            var libDir = _pathService.GetLibrariesDir();
            if (Directory.Exists(libDir))
                totalSize += Directory.GetFiles(libDir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);

            return totalSize;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 删除版本
    /// </summary>
    public bool DeleteVersion(string versionId)
    {
        try
        {
            var versionDir = _pathService.GetVersionDir(versionId);
            if (Directory.Exists(versionDir))
            {
                Directory.Delete(versionDir, recursive: true);
                _versionCache.Remove(versionId);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"删除版本失败 {versionId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 清理无用文件
    /// </summary>
    public async Task<CleanupResult> CleanupUnusedFilesAsync()
    {
        var result = new CleanupResult();

        try
        {
            // 清理重复的依赖库
            await CleanupDuplicateLibrariesAsync(result);

            // 清理临时文件
            CleanupTempFiles(result);

            // 清理旧版本缓存
            await CleanupOldVersionsAsync(result);

            result.Success = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清理文件失败: {ex.Message}");
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 清理重复的依赖库
    /// </summary>
    private async Task CleanupDuplicateLibrariesAsync(CleanupResult result)
    {
        try
        {
            var libDir = _pathService.GetLibrariesDir();
            if (!Directory.Exists(libDir))
                return;

            var libFiles = Directory.GetFiles(libDir, "*.jar", SearchOption.AllDirectories);
            var seen = new HashSet<string>();

            foreach (var file in libFiles)
            {
                var fileName = Path.GetFileName(file);
                var hash = await ComputeFileHashAsync(file);

                var key = $"{fileName}_{hash}";
                if (seen.Contains(key))
                {
                    File.Delete(file);
                    result.FreedSpace += new FileInfo(file).Length;
                    result.DeletedFiles++;
                }
                else
                {
                    seen.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清理重复库失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理临时文件
    /// </summary>
    private void CleanupTempFiles(CleanupResult result)
    {
        try
        {
            var versionsDir = _pathService.GetVersionsDir();
            if (!Directory.Exists(versionsDir))
                return;

            foreach (var versionDir in Directory.GetDirectories(versionsDir))
            {
                var nativesDir = Path.Combine(versionDir, "natives");
                if (Directory.Exists(nativesDir))
                {
                    var tempFiles = Directory.GetFiles(nativesDir, "temp_*.jar");
                    foreach (var tempFile in tempFiles)
                    {
                        File.Delete(tempFile);
                        result.DeletedFiles++;
                        result.FreedSpace += new FileInfo(tempFile).Length;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清理临时文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理旧版本
    /// </summary>
    private async Task CleanupOldVersionsAsync(CleanupResult result)
    {
        // 实现版本清理逻辑
        await Task.CompletedTask;
    }

    /// <summary>
    /// 计算文件哈希
    /// </summary>
    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha1.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    /// <summary>
    /// 提取库信息
    /// </summary>
    private List<LibraryInfo> ExtractLibrariesInfo(JsonArray? libraries)
    {
        var libs = new List<LibraryInfo>();

        if (libraries == null)
            return libs;

        foreach (var lib in libraries)
        {
            var name = lib?["name"]?.ToString() ?? "";
            libs.Add(new LibraryInfo { Name = name });
        }

        return libs;
    }
}

/// <summary>
/// 版本元数据
/// </summary>
public class VersionMetadata
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string ReleaseTime { get; set; } = "";
    public string MainClass { get; set; } = "";
    public string JavaVersion { get; set; } = "";
    public string Assets { get; set; } = "";
    public List<LibraryInfo> Libraries { get; set; } = new();
}

/// <summary>
/// 库信息
/// </summary>
public class LibraryInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Url { get; set; }
}

/// <summary>
/// 版本完整性检查结果
/// </summary>
public class VersionIntegrity
{
    public string VersionId { get; set; } = "";
    public bool IsValid { get; set; } = true;
    public List<string> MissingFiles { get; set; } = new();
    public List<string> MissingLibraries { get; set; } = new();
    public List<string> MissingNatives { get; set; } = new();
    public long TotalSize { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 清理结果
/// </summary>
public class CleanupResult
{
    public bool Success { get; set; }
    public int DeletedFiles { get; set; }
    public long FreedSpace { get; set; }
    public string? Error { get; set; }
}