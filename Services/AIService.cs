using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ourmclauncher.Services;

/// <summary>
/// AI服务 - 支持多种AI服务的日志分析
/// </summary>
public class AIService
{
    private const int MaxLogCharacters = 200_000;
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;

    // 支持的AI服务提供商
    public enum AIProvider
    {
        OpenAI,      // OpenAI (ChatGPT)
        Anthropic,   // Anthropic (Claude)
        DeepSeek,    // DeepSeek (国内常用)
        Custom       // 自定义API端点
    }

    public AIService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    /// <summary>
    /// AI分析结果
    /// </summary>
    public class AIAnalysisResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Summary { get; set; } = "";
        public string CrashReason { get; set; } = "";
        public List<string> Suggestions { get; set; } = new();
        public string TechnicalDetails { get; set; } = "";
        public List<string> RelatedMods { get; set; } = new();
    }

    /// <summary>
    /// 分析游戏崩溃日志
    /// </summary>
    public async Task<AIAnalysisResult> AnalyzeCrashLogAsync(string logContent, string additionalInfo = "")
    {
        var apiKey = _settingsService.GetAIApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            return new AIAnalysisResult
            {
                Success = false,
                ErrorMessage = "请先在AI设置中配置API Key"
            };
        }

        if (logContent.Length > MaxLogCharacters)
        {
            return new AIAnalysisResult
            {
                Success = false,
                ErrorMessage = $"日志过长，请将内容缩减到 {MaxLogCharacters:N0} 个字符以内"
            };
        }

        try
        {
            var provider = GetConfiguredProvider();
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(logContent, additionalInfo);

            var result = await CallAIAPIAsync(apiKey, provider, systemPrompt, userPrompt);
            return ParseAIResponse(result);
        }
        catch (Exception ex)
        {
            return new AIAnalysisResult
            {
                Success = false,
                ErrorMessage = $"AI分析失败: {ex.Message}"
            };
        }
    }

    public AIProvider GetConfiguredProvider()
    {
        if (Enum.TryParse<AIProvider>(
            _settingsService.GetAIProvider(),
            ignoreCase: true,
            out var provider) &&
            provider != AIProvider.Custom)
        {
            return provider;
        }

        return AIProvider.OpenAI;
    }

    /// <summary>
    /// 构建系统提示词
    /// </summary>
    private string BuildSystemPrompt()
    {
        return @"你是一个专业的Minecraft游戏崩溃日志分析专家。你的任务是分析用户提供的游戏崩溃日志，并提供准确的诊断和解决方案。

请按以下格式回复：

## 崩溃原因
[简要描述崩溃的主要原因，如：内存不足、模组冲突、Java版本不兼容等]

## 详细分析
[详细解释导致崩溃的技术原因]

## 解决方案
[提供具体的解决步骤，按优先级排序]
1. [解决方案1]
2. [解决方案2]
3. [解决方案3]

## 可能相关的模组
[如果日志中提到特定模组，列出它们]

## 预防措施
[如何避免类似问题再次发生]

## 技术细节
[提取日志中的关键错误信息、堆栈跟踪、内存使用情况等]

注意：
- 如果日志不完整或关键信息缺失，请说明需要用户提供更多信息
- 如果无法确定确切原因，请列出最可能的原因和建议排查步骤
- 用简洁专业的语言，避免过于技术化的术语
- 优先考虑最常见和最可能的解决方案";
    }

    /// <summary>
    /// 构建用户提示词
    /// </summary>
    private string BuildUserPrompt(string logContent, string additionalInfo)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("请分析以下Minecraft游戏崩溃日志：");
        prompt.AppendLine();
        prompt.AppendLine("=== 崩溃日志 ===");
        prompt.AppendLine(logContent);

        if (!string.IsNullOrEmpty(additionalInfo))
        {
            prompt.AppendLine();
            prompt.AppendLine("=== 附加信息 ===");
            prompt.AppendLine(additionalInfo);
        }

        prompt.AppendLine();
        prompt.AppendLine("请按照上述格式提供分析结果。");
        return prompt.ToString();
    }

    /// <summary>
    /// 调用AI API
    /// </summary>
    private async Task<string> CallAIAPIAsync(string apiKey, AIProvider provider, string systemPrompt, string userPrompt)
    {
        string url;
        string requestBody;

        switch (provider)
        {
            case AIProvider.OpenAI:
                url = "https://api.openai.com/v1/chat/completions";
                requestBody = JsonSerializer.Serialize(new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000
                });
                break;

            case AIProvider.Anthropic:
                url = "https://api.anthropic.com/v1/messages";
                requestBody = JsonSerializer.Serialize(new
                {
                    model = "claude-3-haiku-20240307",
                    max_tokens = 2000,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userPrompt }
                    }
                });
                break;

            case AIProvider.DeepSeek:
                url = "https://api.deepseek.com/chat/completions";
                requestBody = JsonSerializer.Serialize(new
                {
                    model = "deepseek-chat",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000
                });
                break;

            default:
                throw new NotSupportedException($"不支持的AI服务提供商: {provider}");
        }

        // 创建HTTP请求
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(url),
            Method = HttpMethod.Post,
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        // 设置请求头
        switch (provider)
        {
            case AIProvider.OpenAI:
            case AIProvider.DeepSeek:
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                break;
            case AIProvider.Anthropic:
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                break;
        }

        using var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"AI API调用失败: {response.StatusCode} - {responseContent}");
        }

        return ExtractAIResponse(responseContent, provider);
    }

    /// <summary>
    /// 从API响应中提取AI回复内容
    /// </summary>
    internal static string ExtractAIResponse(string responseContent, AIProvider provider)
    {
        try
        {
            var json = JsonDocument.Parse(responseContent);

            switch (provider)
            {
                case AIProvider.OpenAI:
                case AIProvider.DeepSeek:
                    if (json.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.ValueKind == JsonValueKind.Array &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.ToString();
                    }
                    break;

                case AIProvider.Anthropic:
                    if (json.RootElement.TryGetProperty("content", out var anthropicContent) &&
                        anthropicContent.ValueKind == JsonValueKind.Array)
                    {
                        var textParts = anthropicContent
                            .EnumerateArray()
                            .Where(item =>
                                item.TryGetProperty("type", out var type) &&
                                type.GetString() == "text" &&
                                item.TryGetProperty("text", out _))
                            .Select(item => item.GetProperty("text").GetString())
                            .Where(text => !string.IsNullOrEmpty(text));

                        return string.Join("\n", textParts!);
                    }
                    break;
            }

            return "无法解析AI响应";
        }
        catch (Exception ex)
        {
            return $"解析AI响应失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 解析AI响应并转换为结构化结果
    /// </summary>
    private AIAnalysisResult ParseAIResponse(string aiResponse)
    {
        var result = new AIAnalysisResult { Success = true };

        try
        {
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string currentSection = "";

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // 检测章节标题
                if (trimmedLine.StartsWith("##") || trimmedLine.StartsWith("###"))
                {
                    currentSection = trimmedLine.Replace("#", "").Trim();
                    continue;
                }

                // 跳过空行
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                // 根据当前章节解析内容
                switch (currentSection.ToLower())
                {
                    case "崩溃原因":
                        if (!string.IsNullOrEmpty(result.CrashReason))
                            result.CrashReason += " " + trimmedLine;
                        else
                            result.CrashReason = trimmedLine;
                        break;

                    case "详细分析":
                    case "技术细节":
                    case "技术详情":
                        result.TechnicalDetails += trimmedLine + "\n";
                        break;

                    case "解决方案":
                        if (trimmedLine.StartsWith("1.") || trimmedLine.StartsWith("2.") || trimmedLine.StartsWith("3.") ||
                            trimmedLine.StartsWith("4.") || trimmedLine.StartsWith("5.") ||
                            trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*"))
                        {
                            var suggestion = trimmedLine.Substring(trimmedLine.IndexOf('.') + 1).Trim();
                            if (!string.IsNullOrEmpty(suggestion))
                            {
                                result.Suggestions.Add(suggestion.TrimStart('-').TrimStart('*'));
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            result.Suggestions.Add(trimmedLine);
                        }
                        break;

                    case "可能相关的模组":
                    case "相关模组":
                        if (!trimmedLine.StartsWith("可能相关的模组") && !trimmedLine.StartsWith("相关模组"))
                        {
                            result.RelatedMods.Add(trimmedLine.TrimStart('-').TrimStart('*').Trim());
                        }
                        break;

                    case "总结":
                    case "概述": // 如果AI用概述代替总结
                        if (!string.IsNullOrEmpty(result.Summary))
                            result.Summary += " " + trimmedLine;
                        else
                            result.Summary = trimmedLine;
                        break;

                    // 如果没有匹配到任何章节，但内容看起来像总结，添加到summary
                    default:
                        if (string.IsNullOrEmpty(currentSection) && trimmedLine.Length > 20)
                        {
                            if (!string.IsNullOrEmpty(result.Summary))
                                result.Summary += " " + trimmedLine;
                            else
                                result.Summary = trimmedLine;
                        }
                        break;
                }
            }

            // 清理和优化结果
            result.TechnicalDetails = result.TechnicalDetails.Trim();
            result.Summary = result.Summary.Trim();

            // 如果解析后没有任何内容，把原始响应放到Summary中
            if (string.IsNullOrEmpty(result.CrashReason) &&
                string.IsNullOrEmpty(result.Summary) &&
                result.Suggestions.Count == 0)
            {
                result.Summary = aiResponse;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"解析AI响应失败: {ex.Message}";
            result.Summary = aiResponse; // 失败时返回原始内容
        }

        return result;
    }

    /// <summary>
    /// 验证API Key格式
    /// </summary>
    public bool ValidateApiKey(string apiKey, AIProvider provider)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 20)
            return false;

        return provider switch
        {
            AIProvider.Anthropic => apiKey.StartsWith("sk-ant-", StringComparison.Ordinal),
            AIProvider.OpenAI or AIProvider.DeepSeek => apiKey.StartsWith("sk-", StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>
    /// 获取API Key提供商名称
    /// </summary>
    public static string GetProviderName(AIProvider provider)
    {
        return provider switch
        {
            AIProvider.OpenAI => "OpenAI (ChatGPT)",
            AIProvider.Anthropic => "Anthropic (Claude)",
            AIProvider.DeepSeek => "DeepSeek",
            _ => "未知服务"
        };
    }

    /// <summary>
    /// 估算token使用量（粗略估计）
    /// </summary>
    public int EstimateTokens(string text)
    {
        // 粗略估计：英文约4字符=1token，中文约1.5字符=1token
        int chineseChars = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        int otherChars = text.Length - chineseChars;
        return (int)(chineseChars / 1.5 + otherChars / 4);
    }
}
