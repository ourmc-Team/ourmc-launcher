using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 增强下载服务 - 支持并行下载、断点续传、CDN智能选择等PCL/PCL2特性
/// </summary>
public class EnhancedDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly PathService _pathService;

    // CDN源配置
    private readonly List<CdnSource> _cdnSources = new();

    // 下载队列管理
    private readonly ConcurrentQueue<DownloadItem> _downloadQueue = new();
    private readonly ConcurrentDictionary<string, DownloadTask> _activeTasks = new();

    // 并发控制
    private readonly SemaphoreSlim _concurrencySemaphore;
    private const int MAX_CONCURRENT_DOWNLOADS = 4;

    // 断点续传支持
    private readonly ConcurrentDictionary<string, long> _downloadOffsets = new();

    private CancellationTokenSource? _globalCts;

    public EnhancedDownloadService(PathService pathService)
    {
        _pathService = pathService;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "OML-Launcher/2.0");

        _concurrencySemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);

        InitializeCdnSources();
    }

    /// <summary>
    /// 初始化CDN源列表
    /// </summary>
    private void InitializeCdnSources()
    {
        _cdnSources.Clear();
        _cdnSources.Add(new() { Name = "Mojang官方", UrlTemplate = "{0}", Priority = 1, IsAvailable = true });
        _cdnSources.Add(new() { Name = "BMCLAPI", UrlTemplate = "https://bmclapi2.bangbang93.com/{0}", Priority = 2, IsAvailable = true });
        _cdnSources.Add(new() { Name = "Mcbbs", UrlTemplate = "https://download.mcbbs.net/{0}", Priority = 3, IsAvailable = true });
    }

    /// <summary>
    /// 获取版本清单
    /// </summary>
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
                versions.Add(new RemoteVersion
                {
                    Id = v?["id"]?.ToString() ?? "",
                    Type = v?["type"]?.ToString() ?? "",
                    Url = v?["url"]?.ToString() ?? "",
                    ReleaseTime = v?["releaseTime"]?.ToString() ?? ""
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

    /// <summary>
    /// 智能下载版本 - 支持断点续传和CDN选择
    /// </summary>
    public async Task<DownloadTask> DownloadVersionAsync(string versionId, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var task = new DownloadTask
        {
            TaskId = Guid.NewGuid().ToString(),
            VersionId = versionId,
            TaskName = $"下载 Minecraft {versionId}",
            Status = DownloadStatus.Preparing,
            StartTime = DateTime.Now,
            CanResume = true
        };

        _activeTasks[task.TaskId] = task;
        _globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // 1. 获取版本信息
            progress?.Report(new DownloadProgress { Status = "获取版本信息...", OverallProgress = 0 });

            var versionData = await GetVersionDataAsync(versionId, _globalCts.Token);
            if (versionData == null)
            {
                task.Status = DownloadStatus.Failed;
                task.Error = "无法获取版本信息";
                return task;
            }

            // 2. 构建下载队列
            progress?.Report(new DownloadProgress { Status = "分析下载文件...", OverallProgress = 5 });

            var downloadItems = BuildDownloadQueue(versionId, versionData);
            task.TotalFiles = downloadItems.Count;
            task.TotalBytes = downloadItems.Sum(x => x.Size);

            // 3. 开始并行下载
            task.Status = DownloadStatus.Downloading;
            progress?.Report(new DownloadProgress { Status = "开始下载...", OverallProgress = 10 });

            var success = await ProcessDownloadQueueAsync(task, downloadItems, progress, _globalCts.Token);

            if (success)
            {
                task.Status = DownloadStatus.Completed;
                task.EndTime = DateTime.Now;
                progress?.Report(new DownloadProgress { Status = "下载完成!", OverallProgress = 100 });
            }
            else if (_globalCts.Token.IsCancellationRequested)
            {
                task.Status = DownloadStatus.Cancelled;
                task.Error = "下载已取消";
                task.CanResume = true; // 支持断点续传
            }
            else
            {
                task.Status = DownloadStatus.Failed;
                task.Error = "下载失败，部分文件可能损坏";
                task.CanResume = true; // 支持断点续传
            }

            return task;
        }
        catch (OperationCanceledException)
        {
            task.Status = DownloadStatus.Cancelled;
            task.Error = "下载已取消";
            task.CanResume = true;
            return task;
        }
        catch (Exception ex)
        {
            task.Status = DownloadStatus.Failed;
            task.Error = ex.Message;
            Debug.WriteLine($"下载失败: {ex.Message}");
            return task;
        }
    }

    /// <summary>
    /// 获取版本数据
    /// </summary>
    private async Task<JsonNode?> GetVersionDataAsync(string versionId, CancellationToken token)
    {
        try
        {
            // 从官方API获取版本URL
            var manifestJson = await _httpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
            var manifest = JsonNode.Parse(manifestJson);
            var versionsArray = manifest?["versions"] as JsonArray;

            string versionManifestUrl = "";
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
                return null;

            // 下载版本清单
            var versionJson = await _httpClient.GetStringAsync(versionManifestUrl, token);
            var versionData = JsonNode.Parse(versionJson);

            // 保存版本清单
            var versionDir = _pathService.GetVersionDir(versionId);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(versionJsonPath, versionJson, token);

            return versionData;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取版本数据失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 构建下载队列
    /// </summary>
    private List<DownloadItem> BuildDownloadQueue(string versionId, JsonNode versionData)
    {
        var downloadItems = new List<DownloadItem>();

        // 1. 客户端JAR
        var clientDownload = versionData?["downloads"]?["client"];
        if (clientDownload != null)
        {
            downloadItems.Add(new DownloadItem
            {
                Url = clientDownload["url"]?.ToString() ?? "",
                FilePath = _pathService.GetVersionJarPath(versionId),
                Size = clientDownload["size"]?.GetValue<long>() ?? 0,
                Hash = clientDownload["sha1"]?.ToString(),
                DownloadType = "file",
                Priority = 10 // 最高优先级
            });
        }

        // 2. 依赖库
        var libraries = versionData?["libraries"] as JsonArray;
        if (libraries != null)
        {
            var libDir = _pathService.GetLibrariesDir();
            Directory.CreateDirectory(libDir);

            foreach (var lib in libraries)
            {
                var artifact = lib?["downloads"]?["artifact"];
                if (artifact != null)
                {
                    var libPath = artifact["path"]?.ToString();
                    var libUrl = artifact["url"]?.ToString();
                    var libSize = artifact["size"]?.GetValue<long>() ?? 0;

                    if (!string.IsNullOrEmpty(libPath) && !string.IsNullOrEmpty(libUrl))
                    {
                        var fullPath = Path.Combine(libDir, libPath);

                        downloadItems.Add(new DownloadItem
                        {
                            Url = libUrl,
                            FilePath = fullPath,
                            Size = libSize,
                            DownloadType = "library",
                            Priority = 5
                        });
                    }
                }
            }
        }

        // 3. 资源索引
        var assetIndex = versionData?["assetIndex"];
        if (assetIndex != null)
        {
            var assetUrl = assetIndex["url"]?.ToString();
            var assetId = assetIndex["id"]?.ToString();

            if (!string.IsNullOrEmpty(assetUrl) && !string.IsNullOrEmpty(assetId))
            {
                var assetsDir = _pathService.GetAssetIndexesDir();
                Directory.CreateDirectory(assetsDir);
                var assetIndexPath = Path.Combine(assetsDir, $"{assetId}.json");

                downloadItems.Add(new DownloadItem
                {
                    Url = assetUrl,
                    FilePath = assetIndexPath,
                    DownloadType = "asset",
                    Priority = 3
                });
            }
        }

        // 4. Native库
        if (libraries != null)
        {
            var nativesDir = _pathService.GetNativesDir(versionId);
            Directory.CreateDirectory(nativesDir);

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
                    var nativeUrl = nativeArtifact["url"]?.ToString();
                    if (!string.IsNullOrEmpty(nativeUrl))
                    {
                        downloadItems.Add(new DownloadItem
                        {
                            Url = nativeUrl,
                            FilePath = Path.Combine(nativesDir, $"temp_{Guid.NewGuid():N}.jar"),
                            DownloadType = "native",
                            Priority = 7
                        });
                    }
                }
            }
        }

        // 按优先级排序
        return downloadItems.OrderByDescending(x => x.Priority).ToList();
    }

    /// <summary>
    /// 处理下载队列 - 支持并行下载和断点续传
    /// </summary>
    private async Task<bool> ProcessDownloadQueueAsync(DownloadTask task, List<DownloadItem> items, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        var completedCount = 0;
        var totalBytes = items.Sum(x => x.Size);

        // 使用并行下载
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MAX_CONCURRENT_DOWNLOADS,
            CancellationToken = token
        };

        try
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(items, options, item =>
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        // 检查文件是否已存在且完整
                        if (File.Exists(item.FilePath) && IsFileComplete(item))
                        {
                            Interlocked.Increment(ref completedCount);
                            return;
                        }

                        // 选择最佳CDN
                        var cdnUrl = SelectBestCdn(item.Url);

                        // 并行下载控制
                        _concurrencySemaphore.Wait(token);
                        try
                        {
                            // 支持断点续传的下载
                            var success = DownloadFileWithResumeAsync(item, cdnUrl, token).GetAwaiter().GetResult();

                            if (success)
                            {
                                // 验证文件哈希
                                if (!string.IsNullOrEmpty(item.Hash) && !VerifyFileHash(item.FilePath, item.Hash))
                                {
                                    item.Error = "文件校验失败";
                                    item.RetryCount++;

                                    // 重试逻辑
                                    if (item.RetryCount < 3)
                                    {
                                        Debug.WriteLine($"文件校验失败，重试 {item.FilePath}");
                                        File.Delete(item.FilePath);
                                        DownloadFileWithResumeAsync(item, SelectBestCdn(item.Url), token).GetAwaiter().GetResult();
                                    }
                                }

                                Interlocked.Increment(ref completedCount);
                                var currentProgress = (int)((completedCount * 100) / items.Count);

                                progress?.Report(new DownloadProgress
                                {
                                    OverallProgress = currentProgress,
                                    CurrentFile = Path.GetFileName(item.FilePath),
                                    Status = $"下载中 {completedCount}/{items.Count}",
                                    DownloadedFiles = completedCount,
                                    TotalFiles = items.Count
                                });
                            }
                        }
                        finally
                        {
                            _concurrencySemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Error = ex.Message;
                        Debug.WriteLine($"下载文件失败 {item.FilePath}: {ex.Message}");
                    }
                });
            }, token);

            // 处理native库解压
            ExtractNatives(task.VersionId, token);

            return completedCount == items.Count;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 支持断点续传的文件下载
    /// </summary>
    private async Task<bool> DownloadFileWithResumeAsync(DownloadItem item, string url, CancellationToken token)
    {
        try
        {
            var directory = Path.GetDirectoryName(item.FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 检查是否有部分下载的文件
            var offset = 0L;
            if (File.Exists(item.FilePath))
            {
                var fileInfo = new FileInfo(item.FilePath);
                offset = fileInfo.Length;

                // 如果文件大小与预期不符，重新下载
                if (offset >= item.Size && item.Size > 0)
                {
                    File.Delete(item.FilePath);
                    offset = 0;
                }
            }

            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri(url);
            request.Method = HttpMethod.Get;

            // 如果有已下载的部分，添加Range头
            if (offset > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? 0;
            var totalBytes = offset + contentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync(token);
            using var fileStream = new FileStream(item.FilePath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                item.DownloadedBytes += bytesRead;
            }

            item.IsCompleted = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载文件失败 {item.FilePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 选择最佳CDN源
    /// </summary>
    private string SelectBestCdn(string originalUrl)
    {
        // 根据CDN源的优先级和可用性选择
        var availableCdns = _cdnSources.Where(x => x.IsAvailable).OrderByDescending(x => x.Priority).ToList();

        if (availableCdns.Count == 0)
            return originalUrl;

        // 简单的负载均衡：优先级最高且失败次数最少的CDN
        var bestCdn = availableCdns
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.FailureCount)
            .First();

        try
        {
            // 如果是Mojang官方源，直接使用原URL
            if (bestCdn.Name == "Mojang官方")
                return originalUrl;

            // 其他CDN需要URL转换
            var uri = new Uri(originalUrl);
            var relativePath = uri.AbsolutePath.TrimStart('/');
            return string.Format(bestCdn.UrlTemplate, relativePath);
        }
        catch
        {
            return originalUrl;
        }
    }

    /// <summary>
    /// 检查文件是否完整（存在且大小正确）
    /// </summary>
    private bool IsFileComplete(DownloadItem item)
    {
        try
        {
            if (!File.Exists(item.FilePath))
                return false;

            var fileInfo = new FileInfo(item.FilePath);

            // 如果没有指定大小，只检查文件存在
            if (item.Size <= 0)
                return true;

            return fileInfo.Length == item.Size;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 验证文件哈希
    /// </summary>
    private bool VerifyFileHash(string filePath, string expectedHash)
    {
        try
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha1.ComputeHash(stream);
            var actualHash = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return actualHash == expectedHash.ToLower();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解压native库
    /// </summary>
    private void ExtractNatives(string versionId, CancellationToken token)
    {
        try
        {
            var nativesDir = _pathService.GetNativesDir(versionId);
            var tempFiles = Directory.GetFiles(nativesDir, "temp_*.jar");

            foreach (var tempFile in tempFiles)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, nativesDir, overwriteFiles: true);
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"解压native失败 {tempFile}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解压natives失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 取消下载
    /// </summary>
    public void CancelDownload(string taskId)
    {
        _globalCts?.Cancel();
    }

    /// <summary>
    /// 暂停下载
    /// </summary>
    public void PauseDownload(string taskId)
    {
        _globalCts?.Cancel();
    }

    /// <summary>
    /// 获取下载进度
    /// </summary>
    public DownloadProgress? GetDownloadProgress(string taskId)
    {
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            return new DownloadProgress
            {
                OverallProgress = task.CompletedFiles > 0 ? (int)((task.CompletedFiles * 100) / task.TotalFiles) : 0,
                DownloadedFiles = task.CompletedFiles,
                TotalFiles = task.TotalFiles,
                Speed = task.Speed,
                Status = task.Status.ToString()
            };
        }
        return null;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
        _globalCts?.Dispose();
        _concurrencySemaphore?.Dispose();
    }
}
