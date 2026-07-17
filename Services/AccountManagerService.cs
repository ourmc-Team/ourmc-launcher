using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 账号管理服务 - 负责管理多个用户账号
/// </summary>
public class AccountManagerService
{
    private readonly string _accountsFile;
    private List<Account> _accounts = new();

    public AccountManagerService()
        : this(GetDefaultAccountDirectory())
    {
    }

    internal AccountManagerService(string omlDir)
    {
        Directory.CreateDirectory(omlDir);

        _accountsFile = Path.Combine(omlDir, "accounts.json");
        LoadAccounts();
    }

    /// <summary>
    /// 获取所有账号
    /// </summary>
    public List<Account> GetAccounts()
    {
        return new List<Account>(_accounts);
    }

    /// <summary>
    /// 添加账号
    /// </summary>
    public void AddAccount(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.Id))
        {
            account.Id = Guid.NewGuid().ToString("N");
        }

        // 如果是第一个账号，自动设为默认
        if (_accounts.Count == 0)
        {
            account.IsDefault = true;
        }

        account.AddedTime = DateTime.Now;
        _accounts.Add(account);
        SaveAccounts();
    }

    /// <summary>
    /// 更新账号
    /// </summary>
    public void UpdateAccount(Account account)
    {
        var index = _accounts.FindIndex(a => a.Id == account.Id);
        if (index >= 0)
        {
            _accounts[index] = account;
            SaveAccounts();
        }
    }

    /// <summary>
    /// 删除账号
    /// </summary>
    public void RemoveAccount(string accountId)
    {
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            _accounts.Remove(account);

            // 如果删除的是默认账号，将第一个账号设为默认
            if (account.IsDefault && _accounts.Count > 0)
            {
                _accounts[0].IsDefault = true;
            }

            SaveAccounts();
        }
    }

    /// <summary>
    /// 设置默认账号
    /// </summary>
    public void SetDefaultAccount(string accountId)
    {
        foreach (var account in _accounts)
        {
            account.IsDefault = (account.Id == accountId);
        }
        SaveAccounts();
    }

    /// <summary>
    /// 获取默认账号
    /// </summary>
    public Account? GetDefaultAccount()
    {
        return _accounts.FirstOrDefault(a => a.IsDefault);
    }

    /// <summary>
    /// 根据ID获取账号
    /// </summary>
    public Account? GetAccount(string accountId)
    {
        return _accounts.FirstOrDefault(a => a.Id == accountId);
    }

    private void LoadAccounts()
    {
        try
        {
            if (File.Exists(_accountsFile))
            {
                var json = File.ReadAllText(_accountsFile);
                _accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new();
                var migrated = false;
                foreach (var account in _accounts)
                {
                    migrated |= LoadProtectedTokens(account);
                }

                if (migrated)
                {
                    SaveAccounts();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载账号列表失败: {ex.Message}");
            _accounts = new();
        }
    }

    private void SaveAccounts()
    {
        try
        {
            foreach (var account in _accounts)
            {
                account.ProtectedAccessToken = SecretProtectionService.Protect(account.AccessToken);
                account.ProtectedRefreshToken = SecretProtectionService.Protect(account.RefreshToken);
                account.LegacyAccessToken = null;
                account.LegacyRefreshToken = null;
            }

            var json = JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_accountsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存账号列表失败: {ex.Message}");
        }
    }

    private static bool LoadProtectedTokens(Account account)
    {
        var migrated = false;

        if (SecretProtectionService.TryUnprotect(account.ProtectedAccessToken, out var accessToken))
        {
            account.AccessToken = accessToken;
        }

        if (SecretProtectionService.TryUnprotect(account.ProtectedRefreshToken, out var refreshToken))
        {
            account.RefreshToken = refreshToken;
        }

        if (string.IsNullOrEmpty(account.AccessToken) && !string.IsNullOrEmpty(account.LegacyAccessToken))
        {
            account.AccessToken = account.LegacyAccessToken;
            migrated = true;
        }

        if (string.IsNullOrEmpty(account.RefreshToken) && !string.IsNullOrEmpty(account.LegacyRefreshToken))
        {
            account.RefreshToken = account.LegacyRefreshToken;
            migrated = true;
        }

        if (account.LegacyAccessToken != null || account.LegacyRefreshToken != null)
        {
            account.LegacyAccessToken = null;
            account.LegacyRefreshToken = null;
            migrated = true;
        }

        return migrated;
    }

    private static string GetDefaultAccountDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ".minecraft", "oml");
    }
}
