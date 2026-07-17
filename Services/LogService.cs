using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 日志服务 - 负责管理和显示游戏日志
/// </summary>
public class LogService
{
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();
    public event Action<LogEntry>? OnLogAdded;
    public event Action? OnLogsCleared;

    /// <summary>
    /// 获取所有日志
    /// </summary>
    public List<LogEntry> GetLogs()
    {
        lock (_lock)
        {
            return new List<LogEntry>(_logs);
        }
    }

    /// <summary>
    /// 添加日志
    /// </summary>
    public void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Message = message,
            Level = level
        };

        lock (_lock)
        {
            _logs.Add(entry);
        }

        OnLogAdded?.Invoke(entry);
    }

    /// <summary>
    /// 根据游戏输出自动判断日志级别
    /// </summary>
    public void AddGameOutput(string message)
    {
        var level = LogLevel.Info;

        if (string.IsNullOrEmpty(message))
            return;

        var lower = message.ToLower();

        // 检测崩溃
        if (lower.Contains("crash") || lower.Contains("fatal") || lower.Contains("exception"))
            level = LogLevel.Crash;
        // 检测错误
        else if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("unable"))
            level = LogLevel.Error;
        // 检测警告
        else if (lower.Contains("warn") || lower.Contains("deprecated") || lower.Contains("unknown"))
            level = LogLevel.Warning;

        AddLog(message, level);
    }

    /// <summary>
    /// 清空日志
    /// </summary>
    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
        OnLogsCleared?.Invoke();
    }

    /// <summary>
    /// 过滤日志
    /// </summary>
    public List<LogEntry> FilterLogs(LogLevel? level = null, string? search = null)
    {
        lock (_lock)
        {
            var query = _logs.AsEnumerable();

            if (level.HasValue)
            {
                query = query.Where(l => l.Level == level.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(l => l.Message.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }
    }

    /// <summary>
    /// 导出日志到文件
    /// </summary>
    public bool ExportToFile(string path)
    {
        try
        {
            lock (_lock)
            {
                var lines = _logs.Select(l =>
                    $"[{l.Time:HH:mm:ss.fff}] [{l.Level}] {l.Message}"
                );
                File.WriteAllLines(path, lines);
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出日志失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取日志统计
    /// </summary>
    public Dictionary<LogLevel, int> GetLogStats()
    {
        lock (_lock)
        {
            return _logs.GroupBy(l => l.Level)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    /// <summary>
    /// 检查是否有崩溃日志
    /// </summary>
    public bool HasCrashes()
    {
        lock (_lock)
        {
            return _logs.Any(l => l.Level == LogLevel.Crash);
        }
    }

    /// <summary>
    /// 获取最近的崩溃日志
    /// </summary>
    public List<LogEntry> GetRecentCrashes(int count = 10)
    {
        lock (_lock)
        {
            return _logs.Where(l => l.Level == LogLevel.Crash)
                .TakeLast(count)
                .ToList();
        }
    }
}
