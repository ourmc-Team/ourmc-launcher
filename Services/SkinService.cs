using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ourmclauncher.Services;

public class SkinService
{
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private const string BaseUrl = "https://skin.our-mc.cn";
    private string? _authToken;

    public SkinService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "OML Launcher/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<LoginResult> LoginAsync(string identification, string password)
    {
        try
        {
            // 获取 CSRF token 和 session cookie
            var loginPageResponse = await _httpClient.GetAsync("/auth/login");
            var loginPageHtml = await loginPageResponse.Content.ReadAsStringAsync();
            
            // 提取 CSRF token
            string? csrfToken = null;
            
            // 方式1: <meta name="csrf-token" content="...">
            var match1 = Regex.Match(loginPageHtml, @"<meta\s+name=""csrf-token""\s+content=""([^""]+)"""  , RegexOptions.IgnoreCase);
            if (match1.Success) csrfToken = match1.Groups[1].Value;

            var formData = new Dictionary<string, string>
            {
                { "identification", identification },
                { "password", password }
            };

            if (csrfToken != null)
            {
                formData.Add("_token", csrfToken);
            }

            var content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.PostAsync("/auth/login", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            // 检查是否重定向到了用户页面（登录成功）
            if (response.RequestMessage?.RequestUri?.AbsolutePath != "/auth/login")
            {
                // 被重定向了，说明登录成功
                var userInfo = await GetUserInfoFromSessionAsync();
                _authToken = "session";
                return new LoginResult { Success = true, User = userInfo ?? new UserInfo { Nickname = identification } };
            }

            JsonNode? json = null;
            try { json = JsonNode.Parse(responseBody); } catch { }

            var code = json?["code"]?.GetValue<int>();

            if (code == 0)
            {
                var userInfo = await GetUserInfoFromSessionAsync();
                _authToken = "session";
                return new LoginResult { Success = true, User = userInfo ?? new UserInfo { Nickname = identification } };
            }

            var errorMsg = json?["message"]?.ToString() ?? "登录失败";
            return new LoginResult { Success = false, Message = errorMsg };
        }
        catch (Exception ex)
        {
            // 登录失败，返回错误信息
            return new LoginResult { Success = false, Message = $"网络错误: {ex.Message}" };
        }
    }

    private async Task<UserInfo?> GetUserInfoFromSessionAsync()
    {
        try
        {
            // 使用 API 端点获取用户信息（需要 OAuth token，session 可能不行）
            // 尝试从 /user 页面获取
            var response = await _httpClient.GetAsync("/user");
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                
                // 从 HTML 中提取用户信息
                var nicknameMatch = Regex.Match(html, @"nickname[""':\s]+([^""'<,]+)");
                var emailMatch = Regex.Match(html, @"email[""':\s]+([^""'<,]+)");
                var usernameMatch = Regex.Match(html, @"username[""':\s]+([^""'<,]+)");
                var avatarMatch = Regex.Match(html, @"avatar[""':\s]+([^""'<,]+)");

                var userInfo = new UserInfo
                {
                    Nickname = nicknameMatch.Success ? nicknameMatch.Groups[1].Value.Trim().TrimStart('>', ' ') : "",
                    Email = emailMatch.Success ? emailMatch.Groups[1].Value.Trim() : "",
                    Username = usernameMatch.Success ? usernameMatch.Groups[1].Value.Trim() : "",
                    Avatar = avatarMatch.Success ? avatarMatch.Groups[1].Value.Trim() : GetDefaultAvatarUrl(120)
                };

                System.Diagnostics.Debug.WriteLine($"解析用户信息: nickname={userInfo.Nickname}, email={userInfo.Email}");
                
                if (!string.IsNullOrEmpty(userInfo.Nickname) || !string.IsNullOrEmpty(userInfo.Email))
                {
                    return userInfo;
                }
            }
        }
        catch (Exception ex)
        {
            // 获取用户信息失败
            System.Diagnostics.Debug.WriteLine($"获取用户信息失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    public void Logout()
    {
        _authToken = null;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
    }

    /// <summary>
    /// 检查用户是否已登录
    /// </summary>
    public async Task<bool> CheckLoginStatusAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_authToken))
                return true;

            var response = await _httpClient.GetAsync("/api/user");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查登录状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前登录用户的信息
    /// </summary>
    public async Task<UserInfo?> GetUserInfoAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/user");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<UserInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                return userInfo;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取用户信息失败: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// 根据用户名获取用户资料（包括皮肤）
    /// </summary>
    public async Task<UserProfile?> GetUserProfileAsync(string username)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/yggdrasil/api/profiles/minecraft");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                // 成功获取用户资料
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取用户资料失败: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// 获取用户皮肤URL
    /// </summary>
    public string GetSkinUrl(string username)
    {
        return $"{BaseUrl}/skin/{username}.png";
    }

    /// <summary>
    /// 获取用户头像URL
    /// </summary>
    public string GetAvatarUrl(string username)
    {
        return $"{BaseUrl}/avatar/{username}";
    }
    
    /// <summary>
    /// 获取默认头像URL
    /// </summary>
    public string GetDefaultAvatarUrl(int size = 120)
    {
        return $"{BaseUrl}/avatar/0?size={size}";
    }
}

/// <summary>
/// 登录结果
/// </summary>
public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public UserInfo? User { get; set; }
}

/// <summary>
/// 用户信息模型
/// </summary>
public class UserInfo
{
    public string Email { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
}

/// <summary>
/// 用户资料模型
/// </summary>
public class UserProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, TextureProperty> Properties { get; set; } = new();
}

public class TextureProperty
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
