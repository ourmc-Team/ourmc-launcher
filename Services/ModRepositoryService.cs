using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ourmclauncher.Services;

/// <summary>
/// Mod仓库集成服务 - 支持CurseForge和Modrinth
/// 参考了PCL和HMCL的Mod仓库集成
/// </summary>
public class ModRepositoryService
{
    private readonly HttpClient _httpClient = new();

    public ModRepositoryService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ourmc-launcher/1.0");
    }

    /// <summary>
    /// Mod来源
    /// </summary>
    public enum ModSource
    {
        CurseForge,
        Modrinth
    }

    /// <summary>
    /// Mod搜索结果
    /// </summary>
    public class ModSearchResult
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string DownloadCount { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public ModSource Source { get; set; }
        public List<string> Versions { get; set; } = new();
        public string ProjectUrl { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Mod详细信息
    /// </summary>
    public class ModDetails
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Authors { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public string Downloads { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public DateTime DateModified { get; set; }
        public ModSource Source { get; set; }
        public List<ModDependency> Dependencies { get; set; } = new();
        public List<ModVersion> LatestVersions { get; set; } = new();
    }

    /// <summary>
    /// Mod依赖项
    /// </summary>
    public class ModDependency
    {
        public string ModId { get; set; } = "";
        public string ModName { get; set; } = "";
        public string RequiredVersion { get; set; } = "";
        public bool IsRequired { get; set; }
    }

    /// <summary>
    /// Mod版本
    /// </summary>
    public class ModVersion
    {
        public string Id { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public string FileType { get; set; } = "jar";
        public DateTime DatePublished { get; set; }
    }

    /// <summary>
    /// 从Modrinth搜索Mod
    /// </summary>
    public async Task<List<ModSearchResult>> SearchModrinthMods(string query, string gameVersion = "")
    {
        try
        {
            var facets = new List<string[]> { new[] { "project_type:mod" } };
            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                facets.Add(new[] { $"versions:{gameVersion.Trim()}" });
            }

            var url = "https://api.modrinth.com/v2/search" +
                $"?query={Uri.EscapeDataString(query ?? "")}" +
                "&index=relevance&limit=20" +
                $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return ParseModrinthSearchResults(content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Modrinth搜索失败: {ex.Message}");
            return new List<ModSearchResult>();
        }
    }

    internal static List<ModSearchResult> ParseModrinthSearchResults(string content)
    {
        var results = new List<ModSearchResult>();
        var hits = JsonNode.Parse(content)?["hits"] as JsonArray;
        if (hits == null)
        {
            return results;
        }

        foreach (var hit in hits.Take(20))
        {
            if (hit is not JsonObject mod)
            {
                continue;
            }

            var slug = mod["slug"]?.ToString() ?? "";
            _ = DateTime.TryParse(mod["date_modified"]?.ToString(), out var updatedAt);
            results.Add(new ModSearchResult
            {
                Id = mod["project_id"]?.ToString() ?? "",
                Name = mod["title"]?.ToString() ?? "",
                Description = mod["description"]?.ToString() ?? "",
                Author = mod["author"]?.ToString() ?? "",
                DownloadCount = FormatDownloadCount(mod["downloads"]?.GetValue<long>() ?? 0),
                IconUrl = mod["icon_url"]?.ToString() ?? "",
                Source = ModSource.Modrinth,
                Versions = mod["versions"] is JsonArray versions
                    ? versions.Select(version => version?.ToString() ?? "").Where(version => version.Length > 0).ToList()
                    : new List<string>(),
                ProjectUrl = string.IsNullOrEmpty(slug) ? "https://modrinth.com/mods" : $"https://modrinth.com/mod/{slug}",
                UpdatedAt = updatedAt
            });
        }

        return results;
    }

    /// <summary>
    /// 获取Mod详细信息
    /// </summary>
    public async Task<ModDetails?> GetModDetails(string modId, ModSource source)
    {
        try
        {
            if (source == ModSource.Modrinth)
            {
                return await GetModrinthDetails(modId);
            }
            else
            {
                // CurseForge支持（需要API密钥，这里提供基础结构）
                return await GetCurseForgeDetails(modId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取Mod详情失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取Modrinth Mod详情
    /// </summary>
    private async Task<ModDetails?> GetModrinthDetails(string modId)
    {
        var url = $"https://api.modrinth.com/v2/project/{modId}";
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonNode.Parse(content);

        if (data == null) return null;

        var details = new ModDetails
        {
            Id = data["id"]?.ToString() ?? "",
            Name = data["title"]?.ToString() ?? "",
            Summary = data["summary"]?.ToString() ?? "",
            Description = data["description"]?.ToString() ?? "",
            Downloads = FormatDownloadCount(data["downloads"]?.GetValue<long>() ?? 0),
            IconUrl = data["icon_url"]?.ToString() ?? "",
            WebsiteUrl = $"https://modrinth.com/mod/{data["slug"]?.ToString()}",
            DateModified = DateTime.Parse(data["updated"]?.ToString() ?? DateTime.Now.ToString("O"))
        };

        // 作者
        if (data["members"] is JsonArray members)
        {
            foreach (var member in members)
            {
                var username = member?["user"]?["username"]?.ToString();
                if (!string.IsNullOrEmpty(username))
                {
                    details.Authors.Add(username);
                }
            }
        }

        // 分类
        if (data["categories"] is JsonArray categories)
        {
            foreach (var category in categories)
            {
                details.Categories.Add(category?.ToString() ?? "");
            }
        }

        return details;
    }

    /// <summary>
    /// 获取CurseForge Mod详情
    /// </summary>
    private async Task<ModDetails?> GetCurseForgeDetails(string modId)
    {
        // CurseForge API需要特殊处理，这里提供基础结构
        await Task.CompletedTask;
        return new ModDetails
        {
            Id = modId,
            Name = "CurseForge Mod",
            Description = "需要CurseForge API支持",
            Source = ModSource.CurseForge
        };
    }

    /// <summary>
    /// 检测Mod冲突
    /// </summary>
    public async Task<List<string>> DetectConflicts(List<string> modFiles)
    {
        var conflicts = new List<string>();

        // 简化的冲突检测逻辑
        var modNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in modFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);

            // 检测重复文件名（不同版本的同一个Mod）
            if (modNames.Contains(fileName))
            {
                conflicts.Add($"检测到重复Mod: {fileName}");
            }
            modNames.Add(fileName);
        }

        // 检测已知冲突组合
        var knownConflicts = CheckKnownConflicts(modNames);
        conflicts.AddRange(knownConflicts);

        return await Task.FromResult(conflicts);
    }

    /// <summary>
    /// 检查已知Mod冲突
    /// </summary>
    private List<string> CheckKnownConflicts(HashSet<string> modNames)
    {
        var conflicts = new List<string>();

        // 常见冲突示例
        var conflictPairs = new Dictionary<string, string>
        {
            { "optifine", "rubidium" },
            { "tweakermoo", "rubidium" },
            { "betterfps", "optifine" }
        };

        foreach (var pair in conflictPairs)
        {
            if (modNames.Contains(pair.Key) && modNames.Contains(pair.Value))
            {
                conflicts.Add($"已知冲突: {pair.Key} 与 {pair.Value}");
            }
        }

        return conflicts;
    }

    /// <summary>
    /// 格式化下载次数
    /// </summary>
    private static string FormatDownloadCount(long downloads)
    {
        if (downloads >= 1_000_000)
            return $"{Math.Round(downloads / 1_000_000d, 1, MidpointRounding.AwayFromZero):F1}M+";
        if (downloads >= 1_000)
            return $"{Math.Round(downloads / 1_000d, 1, MidpointRounding.AwayFromZero):F1}K+";
        return downloads.ToString();
    }
}
