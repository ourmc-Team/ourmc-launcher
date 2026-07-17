using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 服务器服务 - 负责管理多人游戏服务器列表
/// </summary>
public class ServerService
{
    private readonly string _serversFile;
    private List<ServerInfo> _servers = new();

    public ServerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var omlDir = Path.Combine(appData, ".minecraft", "oml");
        Directory.CreateDirectory(omlDir);

        _serversFile = Path.Combine(omlDir, "servers.json");
        LoadServers();
    }

    /// <summary>
    /// 获取所有服务器
    /// </summary>
    public List<ServerInfo> GetServers()
    {
        return new List<ServerInfo>(_servers);
    }

    /// <summary>
    /// 添加服务器
    /// </summary>
    public void AddServer(ServerInfo server)
    {
        if (string.IsNullOrWhiteSpace(server.Id))
        {
            server.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(server.Name))
        {
            server.Name = server.Address;
        }

        server.AddedTime = DateTime.Now;
        _servers.Add(server);
        SaveServers();
    }

    /// <summary>
    /// 更新服务器
    /// </summary>
    public void UpdateServer(ServerInfo server)
    {
        var index = _servers.FindIndex(s => s.Id == server.Id);
        if (index >= 0)
        {
            _servers[index] = server;
            SaveServers();
        }
    }

    /// <summary>
    /// 删除服务器
    /// </summary>
    public void RemoveServer(string serverId)
    {
        _servers.RemoveAll(s => s.Id == serverId);
        SaveServers();
    }

    /// <summary>
    /// 根据ID获取服务器
    /// </summary>
    public ServerInfo? GetServer(string serverId)
    {
        return _servers.FirstOrDefault(s => s.Id == serverId);
    }

    /// <summary>
    /// 连接服务器（返回服务器地址用于游戏启动参数）
    /// </summary>
    public string? GetServerAddress(string serverId)
    {
        var server = GetServer(serverId);
        return server?.Address;
    }

    private void LoadServers()
    {
        try
        {
            if (File.Exists(_serversFile))
            {
                var json = File.ReadAllText(_serversFile);
                _servers = JsonSerializer.Deserialize<List<ServerInfo>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载服务器列表失败: {ex.Message}");
            _servers = new();
        }
    }

    private void SaveServers()
    {
        try
        {
            var json = JsonSerializer.Serialize(_servers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_serversFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存服务器列表失败: {ex.Message}");
        }
    }
}
