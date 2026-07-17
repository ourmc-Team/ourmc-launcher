using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 下载服务 - 负责从 Mojang 官方源下载游戏文件
/// </summary>
public class DownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly PathService _pathService;
    private readonly object _downloadLock = new();
    private CancellationTokenSource? _cts;

    public DownloadService(PathService pathService)
    {
        _pathService = pathService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "oml-launcher/1.0");
    }

    public bool IsDownloading
    {
        get
        {
            lock (_downloadLock)
            {
                return _cts != null;
            }
        }
    }

    public void CancelDownload()
    {
        lock (_downloadLock)
        {
            _cts?.Cancel();
        }
    }

    public async Task<List<RemoteVersion>> GetVersionManifestAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
            var manifest = JsonNode.Parse(json);
            var versions = new List<RemoteVersion>();

            var versionsArray = manifest?["versions"] as JsonArray;
            if (versionsArray == null) return versions;

            foreach (var v in versionsArray)
            {
                var id = v?["id"]?.ToString() ?? "";
                var type = v?["type"]?.ToString() ?? "";
                var url = v?["url"]?.ToString() ?? "";
                var releaseTime = v?["releaseTime"]?.ToString() ?? "";

                versions.Add(new RemoteVersion
                {
                    Id = id,
                    Type = type,
                    Url = url,
                    ReleaseTime = releaseTime
                });
            }

            return versions;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取版本清单失败: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> DownloadVersionAsync(string versionId, IProgress<(int progress, string status)>? progress = null)
    {
        CancellationTokenSource currentCts;
        lock (_downloadLock)
        {
            if (_cts != null)
            {
                progress?.Report((0, "错误: 已有下载任务正在进行"));
                return false;
            }

            currentCts = new CancellationTokenSource();
            _cts = currentCts;
        }

        var token = currentCts.Token;

        try
        {
            // 1. 获取版本清单
            progress?.Report((0, "获取版本信息..."));

            string versionManifestUrl;
            try
            {
                // 从官方API获取版本URL
                var manifestJson = await _httpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
                var manifest = JsonNode.Parse(manifestJson);
                var versionsArray = manifest?["versions"] as JsonArray;

                versionManifestUrl = "";
                if (versionsArray != null)
                {
                    foreach (var v in versionsArray)
                    {
                        if (v?["id"]?.ToString() == versionId)
                        {
                            versionManifestUrl = v["url"]?.ToString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(versionManifestUrl))
                {
                    progress?.Report((0, "错误: 未找到该版本"));
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                progress?.Report((0, "下载已取消"));
                return false;
            }

            // 2. 下载版本清单
            progress?.Report((5, "下载版本清单..."));
            string versionJson;
            try
            {
                versionJson = await _httpClient.GetStringAsync(versionManifestUrl, token);
            }
            catch (OperationCanceledException)
            {
                progress?.Report((0, "下载已取消"));
                return false;
            }

            // 3. 解析版本清单
            var versionData = JsonNode.Parse(versionJson);
            var clientDownload = versionData?["downloads"]?["client"];
            var clientUrl = clientDownload?["url"]?.ToString();
            var clientSha1 = clientDownload?["sha1"]?.ToString();
            var clientSize = clientDownload?["size"]?.GetValue<long>() ?? 0;

            if (string.IsNullOrEmpty(clientUrl))
            {
                progress?.Report((0, "错误: 无法获取客户端下载链接"));
                return false;
            }

            // 4. 创建版本目录
            var versionDir = _pathService.GetVersionDir(versionId);
            Directory.CreateDirectory(versionDir);

            // 5. 保存版本清单
            progress?.Report((10, "保存版本清单..."));
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(versionJsonPath, versionJson, token);

            // 6. 下载客户端 JAR
            progress?.Report((15, "下载客户端文件..."));
            var clientJarPath = _pathService.GetVersionJarPath(versionId);
            var success = await DownloadFileWithProgressAsync(clientUrl, clientJarPath, clientSize, clientSha1, token,
                (downloadProgress) =>
                {
                    var overallProgress = 15 + (int)(downloadProgress * 0.30);
                    progress?.Report((overallProgress, $"下载客户端... {downloadProgress}%"));
                });
            
            if (!success)
            {
                if (token.IsCancellationRequested)
                {
                    progress?.Report((0, "下载已取消"));
                }
                else
                {
                    progress?.Report((0, "错误: 客户端下载失败"));
                }
                return false;
            }
            
            progress?.Report((45, "客户端文件校验通过"));

            // 7. 下载依赖库
            progress?.Report((50, "处理依赖库..."));
            var libraries = versionData?["libraries"] as JsonArray;
            if (libraries != null)
            {
                var libCount = libraries.Count;
                var libDir = _pathService.GetLibrariesDir();
                Directory.CreateDirectory(libDir);

                for (int i = 0; i < libCount; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var lib = libraries[i];
                    var artifact = lib?["downloads"]?["artifact"];
                    var libUrl = artifact?["url"]?.ToString();
                    var libPath = artifact?["path"]?.ToString();
                    var libSha1 = artifact?["sha1"]?.ToString();
                    var libSize = artifact?["size"]?.GetValue<long>() ?? 0;

                    if (!string.IsNullOrEmpty(libUrl) && !string.IsNullOrEmpty(libPath))
                    {
                        var fullPath = Path.Combine(libDir, libPath);
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        var libProgress = 50 + (int)((i / (double)libCount) * 15);
                        progress?.Report((libProgress, $"下载依赖库 {i + 1}/{libCount}..."));

                        var libraryValid = await DownloadAndVerifyFileAsync(
                            libUrl,
                            fullPath,
                            libSize,
                            libSha1,
                            token);

                        if (!libraryValid)
                        {
                            throw new InvalidDataException($"依赖库下载或校验失败: {libPath}");
                        }
                    }
                }
            }

            // 8. 下载资源索引及资源文件（贴图、音效等）
            progress?.Report((65, "处理资源文件..."));
            await DownloadAssetObjectsAsync(versionData, progress, token);

            // 9. 下载日志配置（log4j2，1.12+ 启动需要）
            progress?.Report((96, "处理日志配置..."));
            await DownloadLogConfigAsync(versionData, token);

            // 10. 提取native库
            progress?.Report((97, "处理本地库..."));
            await ExtractNativesAsync(versionId, versionData, token);

            progress?.Report((100, "下载完成!"));
            return true;
        }
        catch (OperationCanceledException)
        {
            progress?.Report((0, "下载已取消"));
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载版本失败: {ex}");
            progress?.Report((0, $"错误: {ex.Message}"));
            return false;
        }
        finally
        {
            lock (_downloadLock)
            {
                if (ReferenceEquals(_cts, currentCts))
                {
                    _cts = null;
                }
            }

            currentCts.Dispose();
        }
    }

    private async Task ExtractNativesAsync(string versionName, JsonNode? versionData, CancellationToken token)
    {
        try
        {
            var nativesDir = _pathService.GetNativesDir(versionName);
            Directory.CreateDirectory(nativesDir);

            var libraries = versionData?["libraries"] as JsonArray;
            if (libraries == null) return;

            foreach (var lib in libraries)
            {
                token.ThrowIfCancellationRequested();

                var classifiers = lib?["downloads"]?["classifiers"];
                if (classifiers == null) continue;

                JsonNode? nativeArtifact = null;
                if (classifiers["natives-windows"] != null)
                    nativeArtifact = classifiers["natives-windows"];
                else if (classifiers["natives-windows-lgpl"] != null)
                    nativeArtifact = classifiers["natives-windows-lgpl"];

                if (nativeArtifact == null) continue;

                var nativeUrl = nativeArtifact["url"]?.ToString();
                var nativeSha1 = nativeArtifact["sha1"]?.ToString();
                var nativeSize = nativeArtifact["size"]?.GetValue<long>() ?? 0;
                if (string.IsNullOrEmpty(nativeUrl)) continue;

                var tempPath = Path.Combine(nativesDir, $"temp_{Guid.NewGuid():N}.jar");
                try
                {
                    if (!await DownloadAndVerifyFileAsync(nativeUrl, tempPath, nativeSize, nativeSha1, token))
                    {
                        throw new InvalidDataException($"Native 库下载或校验失败: {nativeUrl}");
                    }

                    // 解压 .dll 文件
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, nativesDir, overwriteFiles: true);
                    File.Delete(tempPath);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                    throw new InvalidDataException($"提取 Native 库失败: {nativeUrl}", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("处理 Native 库失败", ex);
        }
    }

    /// <summary>
    /// 下载日志配置文件（logging.client.file），保存到 assets/log_configs/
    /// </summary>
    private async Task DownloadLogConfigAsync(JsonNode? versionData, CancellationToken token)
    {
        var file = versionData?["logging"]?["client"]?["file"];
        if (file == null) return;

        var id = file["id"]?.ToString();
        var url = file["url"]?.ToString();
        var sha1 = file["sha1"]?.ToString();
        var size = file["size"]?.GetValue<long>() ?? 0;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) return;

        var logConfigsDir = Path.Combine(_pathService.GetAssetsDir(), "log_configs");
        Directory.CreateDirectory(logConfigsDir);
        var destPath = Path.Combine(logConfigsDir, id);

        if (!await DownloadAndVerifyFileAsync(url, destPath, size, sha1, token))
        {
            throw new InvalidDataException($"日志配置下载或校验失败: {id}");
        }
    }

    /// <summary>
    /// 下载资源索引及其引用的所有资源文件（贴图、音效等）
    /// </summary>
    private async Task DownloadAssetObjectsAsync(JsonNode? versionData, IProgress<(int progress, string status)>? progress, CancellationToken token)
    {
        var assetIndex = versionData?["assetIndex"];
        var assetUrl = assetIndex?["url"]?.ToString();
        var assetId = assetIndex?["id"]?.ToString();
        var assetSha1 = assetIndex?["sha1"]?.ToString();
        var assetSize = assetIndex?["size"]?.GetValue<long>() ?? 0;

        if (string.IsNullOrEmpty(assetUrl) || string.IsNullOrEmpty(assetId))
        {
            Debug.WriteLine("版本未提供资源索引信息，跳过资源下载");
            return;
        }

        // 1. 确保资源索引文件已下载
        var indexesDir = _pathService.GetAssetIndexesDir();
        Directory.CreateDirectory(indexesDir);
        var assetIndexPath = Path.Combine(indexesDir, $"{assetId}.json");

        if (!await DownloadAndVerifyFileAsync(assetUrl, assetIndexPath, assetSize, assetSha1, token))
        {
            throw new InvalidDataException($"资源索引下载或校验失败: {assetId}");
        }

        // 2. 解析索引，收集需要下载的对象（已存在且大小匹配的跳过，实现增量下载）
        var indexJson = await File.ReadAllTextAsync(assetIndexPath, token);
        var indexData = JsonNode.Parse(indexJson);
        var objects = indexData?["objects"] as JsonObject;
        if (objects == null || objects.Count == 0)
        {
            throw new InvalidDataException($"资源索引内容无效: {assetId}");
        }

        var objectsDir = Path.Combine(_pathService.GetAssetsDir(), "objects");
        Directory.CreateDirectory(objectsDir);

        var pending = new List<(string Hash, long Size, string RelPath)>(objects.Count);
        var pendingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in objects)
        {
            token.ThrowIfCancellationRequested();

            var hash = kv.Value?["hash"]?.ToString();
            var size = kv.Value?["size"]?.GetValue<long>() ?? 0;
            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;

            var relPath = Path.Combine(hash.Substring(0, 2), hash);
            var fullPath = Path.Combine(objectsDir, relPath);

            if (await IsFileValidAsync(fullPath, size, hash, token))
                continue;

            if (pendingHashes.Add(hash))
            {
                pending.Add((hash, size, relPath));
            }
        }

        var total = pending.Count;
        if (total == 0)
        {
            progress?.Report((96, "资源文件已是最新"));
            return;
        }

        Debug.WriteLine($"需要下载 {total} 个资源文件");

        // 3. 并发下载资源对象（每个对象位于 <前两位hash>/<完整hash> 路径下）
        var done = 0;
        var failed = 0;
        var parallelism = Math.Min(8, total);

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = token },
            async (item, ct) =>
            {
                var url = $"https://resources.download.minecraft.net/{item.Hash.Substring(0, 2)}/{item.Hash}";
                var fullPath = Path.Combine(objectsDir, item.RelPath);

                var success = await DownloadAndVerifyFileAsync(
                    url,
                    fullPath,
                    item.Size,
                    item.Hash,
                    ct);

                if (!success)
                {
                    Interlocked.Increment(ref failed);
                    Debug.WriteLine($"资源下载或校验失败: {item.Hash}");
                }

                var completed = Interlocked.Increment(ref done);
                var pct = 67 + (int)((double)completed / total * 29); // 67 -> 96
                var status = $"下载资源文件 {completed}/{total}";
                if (failed > 0) status += $"（失败 {failed}）";
                progress?.Report((pct, status));
            });

        if (failed > 0)
        {
            throw new InvalidDataException($"有 {failed}/{total} 个资源文件下载或校验失败");
        }
    }

    private async Task<bool> DownloadFileWithProgressAsync(
        string url,
        string path,
        long expectedSize,
        string? expectedSha1,
        CancellationToken token,
        Action<int>? progressCallback = null)
    {
        var tempPath = path + ".part";
        try
        {
            if (await IsFileValidAsync(path, expectedSize, expectedSha1, token))
            {
                progressCallback?.Invoke(100);
                return true;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            TryDeleteFile(tempPath);
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync(token);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (int)((downloadedBytes * 100) / totalBytes);
                    progressCallback?.Invoke(progress);
                }
            }

            await fileStream.FlushAsync(token);
            fileStream.Close();

            if (!await IsFileValidAsync(tempPath, expectedSize, expectedSha1, token))
            {
                Debug.WriteLine($"文件长度或 SHA-1 校验失败: {url}");
                TryDeleteFile(tempPath);
                return false;
            }

            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            Debug.WriteLine($"下载文件失败 {url}: {ex.Message}");
            return false;
        }
    }

    private Task<bool> DownloadAndVerifyFileAsync(
        string url,
        string path,
        long expectedSize,
        string? expectedSha1,
        CancellationToken token)
    {
        return DownloadFileWithProgressAsync(url, path, expectedSize, expectedSha1, token);
    }

    private async Task<bool> IsFileValidAsync(
        string path,
        long expectedSize,
        string? expectedSha1,
        CancellationToken token)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (expectedSize > 0 && new FileInfo(path).Length != expectedSize)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedSha1))
        {
            return await VerifyFileHashAsync(path, expectedSha1, token);
        }

        return expectedSize <= 0 || new FileInfo(path).Length == expectedSize;
    }

    /// <summary>
    /// 验证文件 SHA1 哈希值
    /// </summary>
    private static async Task<bool> VerifyFileHashAsync(
        string filePath,
        string expectedSha1,
        CancellationToken token = default)
    {
        try
        {
            using var sha1 = SHA1.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha1.ComputeHashAsync(stream, token);
            return Convert.ToHexString(hash).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup failure must not hide the original download error.
        }
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        CancelDownload();
        _httpClient.Dispose();
    }
}
