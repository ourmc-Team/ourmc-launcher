using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

public sealed class MicrosoftAuthService : IDisposable
{
    internal const string ClientId = "00000000-0000-0000-0000-000000000000";
    private const string MicrosoftScope = "XboxLive.signin offline_access";
    private readonly HttpClient _httpClient = new();

    public class MicrosoftAuthResult
    {
        public bool Success { get; set; }
        public bool Pending { get; set; }
        public string? Message { get; set; }
        public Account? Account { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? Token { get; set; }
        public string? Uhs { get; set; }
        public string? Xuid { get; set; }
        public string? Username { get; set; }
        public string? Uuid { get; set; }
        public string? MinecraftToken { get; set; }
    }

    public async Task<(bool success, string? message, string? deviceCode, string? userCode, string? verificationUri)>
        GetDeviceCodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = MicrosoftScope
            });
            using var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
                form,
                cancellationToken);
            var data = await ReadJsonAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, GetErrorMessage(data, response), null, null, null);
            }

            var deviceCode = data?["device_code"]?.GetValue<string>();
            var userCode = data?["user_code"]?.GetValue<string>();
            var verificationUri = data?["verification_uri"]?.GetValue<string>();
            if (string.IsNullOrEmpty(deviceCode) ||
                string.IsNullOrEmpty(userCode) ||
                string.IsNullOrEmpty(verificationUri))
            {
                return (false, "微软设备码响应缺少必要字段", null, null, null);
            }

            return (true, null, deviceCode, userCode, verificationUri);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取设备代码失败: {ex.Message}");
            return (false, $"网络错误: {ex.Message}", null, null, null);
        }
    }

    public async Task<MicrosoftAuthResult> PollForTokenAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode
            });
            using var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                form,
                cancellationToken);
            var data = await ReadJsonAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = data?["error"]?.GetValue<string>();
                return error switch
                {
                    "authorization_pending" => Failure("等待用户授权...", pending: true),
                    "slow_down" => Failure("请求过于频繁，请稍后重试", pending: true),
                    "authorization_declined" => Failure("用户拒绝了授权"),
                    "expired_token" => Failure("设备代码已过期，请重新登录"),
                    _ => Failure(GetErrorMessage(data, response))
                };
            }

            var accessToken = data?["access_token"]?.GetValue<string>();
            var refreshToken = data?["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                return Failure("微软令牌响应缺少必要字段");
            }

            return await CompleteMinecraftAuthenticationAsync(
                accessToken,
                refreshToken,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"轮询令牌失败: {ex.Message}");
            return Failure($"网络错误: {ex.Message}");
        }
    }

    public async Task<MicrosoftAuthResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = MicrosoftScope
            });
            using var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                form,
                cancellationToken);
            var data = await ReadJsonAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Failure($"刷新令牌失败: {GetErrorMessage(data, response)}");
            }

            var accessToken = data?["access_token"]?.GetValue<string>();
            var newRefreshToken = data?["refresh_token"]?.GetValue<string>() ?? refreshToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                return Failure("刷新响应缺少访问令牌");
            }

            return await CompleteMinecraftAuthenticationAsync(
                accessToken,
                newRefreshToken,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"刷新令牌失败: {ex.Message}");
            return Failure($"网络错误: {ex.Message}");
        }
    }

    private async Task<MicrosoftAuthResult> CompleteMinecraftAuthenticationAsync(
        string microsoftAccessToken,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var xboxResult = await GetXboxLiveTokenAsync(microsoftAccessToken, cancellationToken);
        if (!xboxResult.Success)
        {
            return xboxResult;
        }

        var xstsResult = await GetXstsTokenAsync(xboxResult.Token!, cancellationToken);
        if (!xstsResult.Success)
        {
            return xstsResult;
        }

        var minecraftResult = await GetMinecraftTokenAsync(
            xstsResult.Token!,
            xstsResult.Uhs!,
            cancellationToken);
        if (!minecraftResult.Success)
        {
            return minecraftResult;
        }

        var account = new Account
        {
            Type = AccountType.Microsoft,
            Username = minecraftResult.Username ?? "",
            Nickname = minecraftResult.Username ?? "",
            Uuid = minecraftResult.Uuid ?? "",
            Xuid = xstsResult.Xuid ?? "",
            AccessToken = minecraftResult.MinecraftToken ?? "",
            RefreshToken = refreshToken,
            ExpiresAt = minecraftResult.ExpiresAt
        };

        return new MicrosoftAuthResult
        {
            Success = true,
            Account = account,
            AccessToken = account.AccessToken,
            RefreshToken = refreshToken,
            MinecraftToken = account.AccessToken,
            Username = account.Username,
            Uuid = account.Uuid,
            Xuid = account.Xuid,
            ExpiresAt = account.ExpiresAt
        };
    }

    private async Task<MicrosoftAuthResult> GetXboxLiveTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={accessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var response = await PostJsonAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            requestBody,
            cancellationToken);
        var data = await ReadJsonAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failure($"Xbox Live 认证失败: {GetErrorMessage(data, response)}");
        }

        var token = data?["Token"]?.GetValue<string>();
        var claim = GetFirstXuiClaim(data);
        var uhs = claim?["uhs"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uhs))
        {
            return Failure("Xbox Live 响应缺少令牌或用户哈希");
        }

        return new MicrosoftAuthResult { Success = true, Token = token, Uhs = uhs };
    }

    private async Task<MicrosoftAuthResult> GetXstsTokenAsync(
        string xboxToken,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var response = await PostJsonAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            requestBody,
            cancellationToken);
        var data = await ReadJsonAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var xerr = data?["XErr"]?.ToString();
            return Failure(GetXstsErrorMessage(xerr, data, response));
        }

        var token = data?["Token"]?.GetValue<string>();
        var claim = GetFirstXuiClaim(data);
        var uhs = claim?["uhs"]?.GetValue<string>();
        var xuid = claim?["xid"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uhs))
        {
            return Failure("XSTS 响应缺少令牌或用户哈希");
        }

        return new MicrosoftAuthResult
        {
            Success = true,
            Token = token,
            Uhs = uhs,
            Xuid = xuid
        };
    }

    private async Task<MicrosoftAuthResult> GetMinecraftTokenAsync(
        string xstsToken,
        string uhs,
        CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" },
            cancellationToken);
        var data = await ReadJsonAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failure($"Minecraft 认证失败: {GetErrorMessage(data, response)}");
        }

        var minecraftToken = data?["access_token"]?.GetValue<string>();
        var expiresIn = data?["expires_in"]?.GetValue<int>() ?? 86_400;
        if (string.IsNullOrEmpty(minecraftToken))
        {
            return Failure("Minecraft 响应缺少访问令牌");
        }

        using var profileRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var profileResponse = await _httpClient.SendAsync(profileRequest, cancellationToken);
        var profileData = await ReadJsonAsync(profileResponse, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
        {
            return Failure($"无法获取 Minecraft 档案: {GetErrorMessage(profileData, profileResponse)}");
        }

        var username = profileData?["name"]?.GetValue<string>();
        var uuid = profileData?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(uuid))
        {
            return Failure("Minecraft 档案缺少玩家名或 UUID");
        }

        return new MicrosoftAuthResult
        {
            Success = true,
            Username = username,
            Uuid = uuid,
            MinecraftToken = minecraftToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300))
        };
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        string url,
        object requestBody,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        return await _httpClient.PostAsync(url, content, cancellationToken);
    }

    private static async Task<JsonNode?> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private static JsonNode? GetFirstXuiClaim(JsonNode? data)
    {
        var claims = data?["DisplayClaims"]?["xui"] as JsonArray;
        return claims is { Count: > 0 } ? claims[0] : null;
    }

    private static string GetErrorMessage(JsonNode? data, HttpResponseMessage response)
    {
        return data?["error_description"]?.ToString()
            ?? data?["errorMessage"]?.ToString()
            ?? data?["error"]?.ToString()
            ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    private static string GetXstsErrorMessage(
        string? xerr,
        JsonNode? data,
        HttpResponseMessage response)
    {
        return xerr switch
        {
            "2148916233" => "该微软账号尚未创建 Xbox 账号",
            "2148916235" => "Xbox Live 在当前地区不可用",
            "2148916236" or "2148916237" => "该账号需要完成成人验证",
            "2148916238" => "儿童账号必须加入家庭组才能登录",
            _ => $"XSTS 认证失败: {GetErrorMessage(data, response)}"
        };
    }

    private static MicrosoftAuthResult Failure(string message, bool pending = false)
    {
        return new MicrosoftAuthResult
        {
            Success = false,
            Pending = pending,
            Message = message
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
