using System;

namespace ourmclauncher.Models;

/// <summary>
/// 下载项 - 表示单个文件的下载任务
/// </summary>
public class DownloadItem
{
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long Size { get; set; }
    public string? Hash { get; set; }
    public string DownloadType { get; set; } = "file"; // file, library, asset, native
    public int Priority { get; set; } = 0; // 优先级，数字越大优先级越高
    public int RetryCount { get; set; } = 0;
    public bool IsCompleted { get; set; }
    public long DownloadedBytes { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 下载任务状态
/// </summary>
public class DownloadTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public string VersionId { get; set; } = "";
    public string TaskName { get; set; } = "";
    public DownloadStatus Status { get; set; }
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double Speed { get; set; } // MB/s
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Error { get; set; }
    public bool CanResume { get; set; } // 是否支持断点续传
}

/// <summary>
/// 下载状态枚举
/// </summary>
public enum DownloadStatus
{
    Pending,        // 等待中
    Preparing,      // 准备中
    Downloading,    // 下载中
    Paused,         // 已暂停
    Completed,      // 已完成
    Failed,         // 失败
    Cancelled       // 已取消
}

/// <summary>
/// CDN源信息
/// </summary>
public class CdnSource
{
    public string Name { get; set; } = "";
    public string UrlTemplate { get; set; } = "";
    public int Priority { get; set; } = 0;
    public bool IsAvailable { get; set; } = true;
    public double AverageSpeed { get; set; } // 平均速度 (MB/s)
    public int FailureCount { get; set; }
}

/// <summary>
/// 下载进度信息
/// </summary>
public class DownloadProgress
{
    public int OverallProgress { get; set; } // 总进度 0-100
    public string CurrentFile { get; set; } = "";
    public int CurrentFileProgress { get; set; } // 当前文件进度 0-100
    public double Speed { get; set; } // MB/s
    public string Status { get; set; } = "";
    public int DownloadedFiles { get; set; }
    public int TotalFiles { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
}