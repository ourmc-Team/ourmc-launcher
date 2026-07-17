using System.Text.Json;
using ourmclauncher.Models;
using ourmclauncher.Services;
using Xunit;

namespace ourmclauncher.Tests;

public sealed class SecurityAndAIServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "oml-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecretProtectionRoundTripsForCurrentWindowsUser()
    {
        const string secret = "secret-value-for-dpapi-test";

        var encrypted = SecretProtectionService.Protect(secret);
        var success = SecretProtectionService.TryUnprotect(encrypted, out var decrypted);

        Assert.True(success);
        Assert.Equal(secret, decrypted);
        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsServiceMigratesLegacyApiKeyWithoutLeavingPlaintext()
    {
        Directory.CreateDirectory(_tempDirectory);
        const string secret = "sk-legacy-secret-value-123456789";
        File.WriteAllText(
            Path.Combine(_tempDirectory, "settings.json"),
            $$"""{"AIApiKey":"{{secret}}","AIProvider":"Anthropic"}""");

        var settingsService = new SettingsService(_tempDirectory);
        var persistedJson = File.ReadAllText(Path.Combine(_tempDirectory, "settings.json"));

        Assert.Equal(secret, settingsService.GetAIApiKey());
        Assert.Equal("Anthropic", settingsService.GetAIProvider());
        Assert.DoesNotContain(secret, persistedJson, StringComparison.Ordinal);
        Assert.Contains("ProtectedAIApiKey", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountManagerMigratesLegacyTokensWithoutLeavingPlaintext()
    {
        Directory.CreateDirectory(_tempDirectory);
        const string accessToken = "minecraft-access-token";
        const string refreshToken = "microsoft-refresh-token";
        File.WriteAllText(
            Path.Combine(_tempDirectory, "accounts.json"),
            $$"""[{"Id":"account-1","Username":"Player","AccessToken":"{{accessToken}}","RefreshToken":"{{refreshToken}}"}]""");

        var accountManager = new AccountManagerService(_tempDirectory);
        var account = Assert.Single(accountManager.GetAccounts());
        var persistedJson = File.ReadAllText(Path.Combine(_tempDirectory, "accounts.json"));

        Assert.Equal(accessToken, account.AccessToken);
        Assert.Equal(refreshToken, account.RefreshToken);
        Assert.DoesNotContain(accessToken, persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, persistedJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AIService.AIProvider.OpenAI, "{\"choices\":[{\"message\":{\"content\":\"openai\"}}]}", "openai")]
    [InlineData(AIService.AIProvider.DeepSeek, "{\"choices\":[{\"message\":{\"content\":\"deepseek\"}}]}", "deepseek")]
    [InlineData(AIService.AIProvider.Anthropic, "{\"content\":[{\"type\":\"text\",\"text\":\"claude\"}]}", "claude")]
    public void ExtractAIResponseSupportsConfiguredProvider(
        AIService.AIProvider provider,
        string response,
        string expected)
    {
        Assert.Equal(expected, AIService.ExtractAIResponse(response, provider));
    }

    [Fact]
    public void ModelsDoNotSerializePlaintextSecrets()
    {
        var settingsJson = JsonSerializer.Serialize(new AppSettings { AIApiKey = "plain-api-key" });
        var accountJson = JsonSerializer.Serialize(new Account
        {
            AccessToken = "plain-access-token",
            RefreshToken = "plain-refresh-token"
        });

        Assert.DoesNotContain("plain-api-key", settingsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-access-token", accountJson, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-refresh-token", accountJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ModrinthSearchParserReadsHitFieldsDirectly()
    {
        const string response = """
            {"hits":[{"project_id":"abc","title":"Sodium","description":"Fast rendering","author":"jellysquid3","downloads":1250000,"icon_url":"https://example/icon.png","slug":"sodium","date_modified":"2026-01-02T03:04:05Z","versions":["1.21.1","1.21"]}]}
            """;

        var result = Assert.Single(ModRepositoryService.ParseModrinthSearchResults(response));

        Assert.Equal("abc", result.Id);
        Assert.Equal("Sodium", result.Name);
        Assert.Equal("jellysquid3", result.Author);
        Assert.Equal("1.3M+", result.DownloadCount);
        Assert.Equal(new[] { "1.21.1", "1.21" }, result.Versions);
        Assert.Equal("https://modrinth.com/mod/sodium", result.ProjectUrl);
    }

    [Theory]
    [InlineData("1.16.5-forge", 8)]
    [InlineData("fabric-loader-1.17.1", 16)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.1", 21)]
    public void JavaFallbackMatchesMinecraftRequirements(string versionName, int expected)
    {
        Assert.Equal(expected, EnvironmentService.GetFallbackJavaVersion(versionName));
    }

    [Fact]
    public void CustomJvmArgsCannotOverrideManagedMemoryArguments()
    {
        var tokens = LaunchService.GetCustomJvmTokens(
            "-Xmx8G -Dfile.encoding=UTF-8 -Xms2G \"-Dexample=value with spaces\"");

        Assert.Equal(
            new[] { "-Dfile.encoding=UTF-8", "-Dexample=value with spaces" },
            tokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
