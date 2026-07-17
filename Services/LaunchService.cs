using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

/// <summary>
/// 游戏启动服务 - 负责构建启动参数和启动Minecraft进程
/// </summary>
public class LaunchService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly PathService _pathService;
    private readonly EnvironmentService _environmentService;
    private Process? _gameProcess;

    public LaunchService(SettingsService settings, PathService pathService, EnvironmentService environmentService)
    {
        _settings = settings;
        _pathService = pathService;
        _environmentService = environmentService;
    }

    public event Action<string>? OnLaunchOutput;
    public event Action<string>? OnLaunchError;
    public event Action<EnvironmentService.EnvironmentReport>? OnEnvironmentCheck;

    /// <summary>
    /// 检查游戏是否正在运行
    /// </summary>
    public bool IsGameRunning => _gameProcess != null && !_gameProcess.HasExited;

    public bool Launch(
        string versionName,
        string? playerName = null,
        int maxMemory = 2048,
        bool skipEnvironmentCheck = false,
        Account? account = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"=== 开始启动游戏 {versionName} ===");

            // 环境检查
            if (!skipEnvironmentCheck)
            {
                var environmentReport = _environmentService.CheckEnvironment(versionName);
                OnEnvironmentCheck?.Invoke(environmentReport);

                if (!environmentReport.CanLaunch)
                {
                    var error = $"环境检查失败: {environmentReport.GetSummary()}";
                    System.Diagnostics.Debug.WriteLine(error);
                    OnLaunchError?.Invoke(error);
                    return false;
                }

                // 如果有警告，输出到日志
                var warnings = environmentReport.Checks.Where(c => c.Level == EnvironmentService.EnvironmentCheckLevel.Warning);
                foreach (var warning in warnings)
                {
                    System.Diagnostics.Debug.WriteLine($"[环境警告] {warning.ErrorMessage}");
                }
            }

            var gameDir = _pathService.GameDir;
            var versionDir = _pathService.GetVersionDir(versionName);
            var jarPath = _pathService.GetVersionJarPath(versionName);
            var jsonPath = _pathService.GetVersionJsonPath(versionName);

            System.Diagnostics.Debug.WriteLine($"游戏目录: {gameDir}");
            System.Diagnostics.Debug.WriteLine($"版本目录: {versionDir}");
            System.Diagnostics.Debug.WriteLine($"JAR路径: {jarPath}");
            System.Diagnostics.Debug.WriteLine($"JSON路径: {jsonPath}");

            if (!File.Exists(jsonPath))
            {
                var error = $"找不到版本清单: {jsonPath}";
                System.Diagnostics.Debug.WriteLine(error);
                OnLaunchError?.Invoke(error);
                return false;
            }

            var javaPath = _settings.GetJavaPath();
            System.Diagnostics.Debug.WriteLine($"配置的Java路径: {javaPath}");

            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                System.Diagnostics.Debug.WriteLine("Java路径无效，尝试自动检测...");
                javaPath = AutoDetectJava();
                if (string.IsNullOrEmpty(javaPath))
                {
                    var error = "未找到Java，请在设置中配置Java路径";
                    System.Diagnostics.Debug.WriteLine(error);
                    OnLaunchError?.Invoke(error);
                    return false;
                }
                System.Diagnostics.Debug.WriteLine($"自动检测到Java: {javaPath}");
            }

            System.Diagnostics.Debug.WriteLine("开始构建启动参数...");
            var arguments = BuildLaunchArguments(
                javaPath,
                gameDir,
                versionDir,
                jarPath,
                jsonPath,
                versionName,
                playerName,
                maxMemory,
                account);
            if (arguments == null)
            {
                var error = "构建启动参数失败";
                System.Diagnostics.Debug.WriteLine(error);
                OnLaunchError?.Invoke(error);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"Java路径: {javaPath}");
            System.Diagnostics.Debug.WriteLine("启动参数构建完成");

            _gameProcess = new Process();
            _gameProcess.StartInfo.FileName = javaPath;
            _gameProcess.StartInfo.Arguments = arguments;
            _gameProcess.StartInfo.WorkingDirectory = gameDir;
            _gameProcess.StartInfo.UseShellExecute = false;
            _gameProcess.StartInfo.RedirectStandardOutput = true;
            _gameProcess.StartInfo.RedirectStandardError = true;
            _gameProcess.StartInfo.CreateNoWindow = true;

            _gameProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[游戏输出] {e.Data}");
                    OnLaunchOutput?.Invoke(e.Data);
                }
            };

            _gameProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[游戏错误] {e.Data}");
                    OnLaunchError?.Invoke(e.Data);
                }
            };

            System.Diagnostics.Debug.WriteLine("启动游戏进程...");
            _gameProcess.Start();
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();

            System.Diagnostics.Debug.WriteLine($"游戏进程已启动，PID: {_gameProcess.Id}");
            return true;
        }
        catch (Exception ex)
        {
            var error = $"启动失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(error);
            System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            OnLaunchError?.Invoke(error);
            return false;
        }
    }

    private string? BuildLaunchArguments(
        string javaPath,
        string gameDir,
        string versionDir,
        string jarPath,
        string jsonPath,
        string versionName,
        string? playerName,
        int maxMemory,
        Account? account)
    {
        try
        {
            var versionNode = JsonNode.Parse(File.ReadAllText(jsonPath));
            if (versionNode == null)
            {
                Debug.WriteLine("版本 JSON 解析失败");
                return null;
            }

            // 解析继承链（Forge/Fabric 等），合并 mainClass、libraries、arguments
            var merged = ResolveVersionChain(versionNode, jsonPath);

            if (string.IsNullOrEmpty(merged.MainClass))
            {
                Debug.WriteLine("缺少 mainClass，无法启动");
                return null;
            }

            var librariesDir = _pathService.GetLibrariesDir();
            var nativesDir = _pathService.GetNativesDir(versionName);
            var assetsRoot = Path.Combine(gameDir, "assets");
            var isMicrosoftAccount = account is
            {
                Type: AccountType.Microsoft,
                AccessToken.Length: > 0,
                Uuid.Length: > 0,
                Username.Length: > 0
            };
            var playerNameValue = isMicrosoftAccount ? account!.Username : playerName ?? "Player";
            var uuid = isMicrosoftAccount
                ? account!.Uuid.Replace("-", "", StringComparison.Ordinal)
                : OfflineUuid(playerNameValue);
            var accessToken = isMicrosoftAccount ? account!.AccessToken : "0";
            var assetId = merged.AssetId ?? "legacy";

            // 构建 classpath（版本 jar + 父版本 jar + 依赖库）
            var classpath = BuildClassPath(gameDir, jarPath, merged);

            // 变量替换表（涵盖现代 arguments 与旧版 minecraftArguments 用到的全部占位符）
            var vars = new Dictionary<string, string>
            {
                ["${auth_access_token}"] = accessToken,
                ["${auth_session}"] = $"token:{accessToken}:{uuid}",
                ["${auth_player_name}"] = playerNameValue,
                ["${auth_uuid}"] = uuid,
                ["${clientid}"] = isMicrosoftAccount ? MicrosoftAuthService.ClientId : "",
                ["${auth_xuid}"] = isMicrosoftAccount ? account!.Xuid : "",
                ["${quickPlaySingleplayer}"] = "",
                ["${quickPlayMultiplayer}"] = "",
                ["${quickPlayRealms}"] = "",
                ["${quickPlayPath}"] = "",
                ["${assets_index_name}"] = assetId,
                ["${assets_root}"] = assetsRoot,
                ["${game_assets}"] = Path.Combine(assetsRoot, "virtual", assetId),
                ["${game_directory}"] = gameDir,
                ["${user_properties}"] = "{}",
                ["${user_type}"] = isMicrosoftAccount ? "msa" : "mojang",
                ["${version_name}"] = versionName,
                ["${version_type}"] = "oml",
                ["${natives_directory}"] = nativesDir,
                ["${library_directory}"] = librariesDir,
                ["${classpath_separator}"] = ";",
                ["${classpath}"] = classpath,
                ["${launcher_name}"] = "ourmc-launcher",
                ["${launcher_version}"] = "1.0",
                ["${resolution_width}"] = _settings.GetWindowWidth().ToString(),
                ["${resolution_height}"] = _settings.GetWindowHeight().ToString(),
            };

            // ===== JVM 参数 =====
            var jvmTokens = new List<string>
            {
                $"-Xmx{maxMemory}M",
                "-Xms512M",
            };

            // 用户自定义 JVM 参数
            var customJvmArgs = _settings.GetCustomJvmArgs();
            if (!string.IsNullOrWhiteSpace(customJvmArgs))
                jvmTokens.AddRange(GetCustomJvmTokens(customJvmArgs));

            // 版本定义的 JVM 参数（现代 arguments.jvm；旧版则使用默认值）
            foreach (var entry in GetJvmArgumentEntries(merged))
                foreach (var s in ExtractArgValue(entry))
                    jvmTokens.Add(Substitute(s, vars));

            // log4j 配置参数（1.12+ 需要，文件存在时才追加）
            var logArg = BuildLogConfigArgument(merged, assetsRoot);
            if (logArg != null)
                jvmTokens.Add(logArg);

            // ===== 游戏参数 =====
            var gameTokens = new List<string>();
            foreach (var entry in GetGameArgumentEntries(merged))
                foreach (var s in ExtractArgValue(entry))
                    gameTokens.Add(Substitute(s, vars));

            if (_settings.GetFullscreen())
            {
                gameTokens.Add("--fullscreen");
            }
            else
            {
                gameTokens.Add("--width");
                gameTokens.Add(_settings.GetWindowWidth().ToString());
                gameTokens.Add("--height");
                gameTokens.Add(_settings.GetWindowHeight().ToString());
            }

            // 组装：jvm 参数 + 主类 + 游戏参数
            var allTokens = new List<string>();
            allTokens.AddRange(jvmTokens);
            allTokens.Add(merged.MainClass!);
            allTokens.AddRange(gameTokens);

            var result = string.Join(" ", allTokens.Select(QuoteIfNeeded));
            if (result.Contains("${"))
                Debug.WriteLine($"[警告] 启动参数中存在未解析的变量: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"构建启动参数失败: {ex.Message}");
            Debug.WriteLine($"堆栈: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// 合并版本继承链，得到最终的 mainClass / libraries / arguments 等
    /// </summary>
    private MergedVersion ResolveVersionChain(JsonNode versionNode, string jsonPath)
    {
        var merged = new MergedVersion();
        var versionsDir = _pathService.GetVersionsDir();

        // 收集继承链：子版本 -> 父版本 -> ...
        var chain = new List<JsonNode> { versionNode };
        var inheritId = versionNode["inheritsFrom"]?.ToString();
        var guard = new HashSet<string>();
        while (!string.IsNullOrEmpty(inheritId) && guard.Add(inheritId))
        {
            var parentJsonPath = Path.Combine(versionsDir, inheritId, $"{inheritId}.json");
            if (!File.Exists(parentJsonPath))
            {
                Debug.WriteLine($"父版本 JSON 不存在: {parentJsonPath}");
                break;
            }

            var parent = JsonNode.Parse(File.ReadAllText(parentJsonPath));
            if (parent == null) break;
            chain.Add(parent);

            // 父版本的 jar 加入 classpath
            var parentJar = Path.Combine(versionsDir, inheritId, $"{inheritId}.jar");
            if (File.Exists(parentJar)) merged.ParentJars.Add(parentJar);

            inheritId = parent["inheritsFrom"]?.ToString();
        }

        // 从最老的祖先向子版本合并，使子版本覆盖父版本
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var node = chain[i];

            if (node["mainClass"]?.ToString() is { } mc && !string.IsNullOrEmpty(mc))
                merged.MainClass = mc;

            if (node["assetIndex"]?["id"]?.ToString() is { } aid && !string.IsNullOrEmpty(aid))
                merged.AssetId = aid;
            else if (merged.AssetId == null && node["assets"]?.ToString() is { } a && !string.IsNullOrEmpty(a))
                merged.AssetId = a;

            if (node["libraries"] is JsonArray libs)
                foreach (var lib in libs) if (lib != null) merged.Libraries.Add(lib);

            if (node["arguments"]?["game"] is JsonArray gameArr)
                foreach (var g in gameArr) if (g != null) merged.GameArguments.Add(g);
            if (node["arguments"]?["jvm"] is JsonArray jvmArr)
                foreach (var j in jvmArr) if (j != null) merged.JvmArguments.Add(j);

            if (node["minecraftArguments"]?.ToString() is { } legacy && !string.IsNullOrEmpty(legacy))
                merged.LegacyArguments = legacy;

            if (node["logging"]?["client"] is JsonObject logClient)
                merged.LogConfig = logClient;
        }

        return merged;
    }

    private IEnumerable<JsonNode> GetJvmArgumentEntries(MergedVersion merged)
    {
        if (merged.JvmArguments.Count > 0)
            return merged.JvmArguments;

        // 旧版（无 arguments.jvm）使用默认 JVM 参数
        return new JsonNode[]
        {
            JsonValue.Create($"-Djava.library.path=${{natives_directory}}"),
            JsonValue.Create($"-Dminecraft.launcher.brand=${{launcher_name}}"),
            JsonValue.Create($"-Dminecraft.launcher.version=${{launcher_version}}"),
            JsonValue.Create("-cp"),
            JsonValue.Create("${classpath}"),
        };
    }

    private IEnumerable<JsonNode> GetGameArgumentEntries(MergedVersion merged)
    {
        if (merged.GameArguments.Count > 0)
            return merged.GameArguments;

        if (!string.IsNullOrEmpty(merged.LegacyArguments))
            return merged.LegacyArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => (JsonNode)JsonValue.Create(p));

        return Array.Empty<JsonNode>();
    }

    /// <summary>
    /// 从一条 argument 条目中提取有效值（处理 rules 守卫与 value 为数组的情况）
    /// </summary>
    private List<string> ExtractArgValue(JsonNode entry)
    {
        var result = new List<string>();
        switch (entry)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                result.Add(s);
                break;
            case JsonObject obj:
                if (obj["rules"] is JsonArray rules && !CheckRules(rules))
                    break;
                switch (obj["value"])
                {
                    case JsonValue vv when vv.TryGetValue<string>(out var vs):
                        result.Add(vs);
                        break;
                    case JsonArray arr:
                        foreach (var item in arr)
                            if (item is JsonValue iv && iv.TryGetValue<string>(out var istr))
                                result.Add(istr);
                        break;
                }
                break;
        }
        return result;
    }

    /// <summary>
    /// 评估库/参数的 OS 规则。无规则=允许；有规则时按“最后一条匹配的规则”决定。
    /// </summary>
    private bool CheckRules(JsonArray rules)
    {
        if (rules.Count == 0) return true;
        bool allowed = false;
        foreach (var r in rules)
        {
            if (r is not JsonObject rule || !RuleMatches(rule)) continue;
            allowed = rule["action"]?.ToString() == "allow";
        }
        return allowed;
    }

    private bool RuleMatches(JsonObject rule)
    {
        // 本启动器不启用 feature（demo/自定义分辨率）相关规则
        if (rule["features"] is JsonObject) return false;

        if (rule["os"] is JsonObject os)
        {
            var name = os["name"]?.ToString();
            if (!string.IsNullOrEmpty(name) && name != "windows") return false;

            var arch = os["arch"]?.ToString();
            if (!string.IsNullOrEmpty(arch) && arch != CurrentOsArch) return false;

            var ver = os["version"]?.ToString();
            if (!string.IsNullOrEmpty(ver))
            {
                try { if (!Regex.IsMatch(CurrentWindowsVersion, ver)) return false; }
                catch (ArgumentException) { /* 非法正则，忽略 */ }
            }
        }

        return true;
    }

    private string BuildClassPath(string gameDir, string jarPath, MergedVersion merged)
    {
        var entries = new List<string> { jarPath };

        foreach (var parentJar in merged.ParentJars)
            if (!entries.Contains(parentJar)) entries.Add(parentJar);

        foreach (var lib in merged.Libraries)
        {
            if (lib is not JsonObject libObj) continue;
            if (libObj["rules"] is JsonArray rules && !CheckRules(rules))
                continue;

            // 优先用 downloads.artifact.path；缺失时由 name 推断（部分 modded 库）
            var path = libObj["downloads"]?["artifact"]?["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                path = LibraryNameToPath(libObj["name"]?.ToString());
            if (string.IsNullOrEmpty(path)) continue;

            var fullPath = Path.Combine(gameDir, "libraries", path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath) && !entries.Contains(fullPath))
                entries.Add(fullPath);
        }

        return string.Join(";", entries);
    }

    /// <summary>
    /// 由 Maven 坐标 (group:artifact:version[:classifier]) 推断库文件相对路径
    /// </summary>
    private static string? LibraryNameToPath(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var parts = name.Split(':');
        if (parts.Length < 3) return null;

        var pkg = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{pkg}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

    private static string Substitute(string s, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('$') < 0) return s;
        foreach (var kv in vars)
        {
            if (s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                s = s.Replace(kv.Key, kv.Value);
        }
        return s;
    }

    /// <summary>
    /// 生成离线模式的玩家 UUID（与 Java UUID.nameUUIDFromBytes("OfflinePlayer:"+name) 一致，
    /// 保证单机存档/玩家数据与离线登录服务器一致）
    /// </summary>
    private static string OfflineUuid(string username)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
            byte[] hash;
            using (var md5 = MD5.Create())
                hash = md5.ComputeHash(bytes);

            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version = 3
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant = IETF

            var sb = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10) sb.Append('-');
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
        catch
        {
            return "00000000-0000-0000-0000-000000000000";
        }
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOf('"') >= 0) return arg;       // 已含引号，原样返回
        if (arg.IndexOf(' ') >= 0) return "\"" + arg + "\"";
        return arg;
    }

    private static List<string> SplitArgs(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in s)
        {
            if (ch == '"') inQuote = !inQuote;
            else if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    public static IReadOnlyList<string> GetCustomJvmTokens(string? customJvmArgs)
    {
        if (string.IsNullOrWhiteSpace(customJvmArgs))
        {
            return Array.Empty<string>();
        }

        return SplitArgs(customJvmArgs.Trim())
            .Where(token =>
                !token.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) &&
                !token.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 构建 log4j 配置参数；仅当配置文件已下载时返回参数字符串，否则返回 null（不阻塞启动）
    /// </summary>
    private static string? BuildLogConfigArgument(MergedVersion merged, string assetsRoot)
    {
        var logClient = merged.LogConfig;
        if (logClient == null) return null;

        var fileId = logClient["file"]?["id"]?.ToString();
        var argTemplate = logClient["argument"]?.ToString();
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(argTemplate)) return null;

        var logPath = Path.Combine(assetsRoot, "log_configs", fileId);
        if (!File.Exists(logPath))
        {
            Debug.WriteLine($"log4j 配置文件缺失，跳过: {logPath}");
            return null;
        }

        return argTemplate.Replace("${path}", logPath);
    }

    // Minecraft 用 "x86" 表示 x86-64（ARM64 才是 "arm64"）
    private static readonly string CurrentOsArch = "x86";
    private static readonly string CurrentWindowsVersion = Environment.OSVersion.Version.ToString();

    private class MergedVersion
    {
        public string? MainClass;
        public string? AssetId;
        public List<JsonNode> Libraries = new();
        public List<JsonNode> JvmArguments = new();
        public List<JsonNode> GameArguments = new();
        public string? LegacyArguments;   // minecraftArguments（旧版）
        public List<string> ParentJars = new();
        public JsonObject? LogConfig;     // logging.client
    }

    public string? AutoDetectJava()
    {
        System.Diagnostics.Debug.WriteLine("开始自动检测Java...");

        // 1. 检查常见的Java安装目录
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Java"),
        };

        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir))
            {
                System.Diagnostics.Debug.WriteLine($"目录不存在: {dir}");
                continue;
            }

            System.Diagnostics.Debug.WriteLine($"检查目录: {dir}");
            foreach (var jvmDir in Directory.GetDirectories(dir))
            {
                var javaw = Path.Combine(jvmDir, "bin", "javaw.exe");
                var java = Path.Combine(jvmDir, "bin", "java.exe");
                System.Diagnostics.Debug.WriteLine($"检查Java: {javaw}");

                if (File.Exists(javaw))
                {
                    System.Diagnostics.Debug.WriteLine($"找到Java: {javaw}");
                    return javaw;
                }
                if (File.Exists(java))
                {
                    System.Diagnostics.Debug.WriteLine($"找到Java: {java}");
                    return java;
                }
            }
        }

        // 2. 检查系统PATH中的java
        try
        {
            System.Diagnostics.Debug.WriteLine("检查系统PATH中的java...");
            var psi = new ProcessStartInfo("where", "java")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (File.Exists(line.Trim()))
                        {
                            System.Diagnostics.Debug.WriteLine($"从PATH找到Java: {line.Trim()}");
                            return line.Trim();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("检查PATH失败: " + ex.Message);
        }

        // 3. 检查常见的JDK安装路径
        var jdkPaths = new[]
        {
            @"C:\Program Files\Java\jdk-*",
            @"C:\Program Files\Java\jre-*",
            @"C:\Program Files (x86)\Java\jdk-*",
            @"C:\Program Files (x86)\Java\jre-*",
            @"C:\Java\*",
        };

        foreach (var pattern in jdkPaths)
        {
            try
            {
                var directory = Path.GetDirectoryName(pattern);
                var searchPattern = Path.GetFileName(pattern);

                if (Directory.Exists(directory))
                {
                    var dirs = Directory.GetDirectories(directory, searchPattern.Replace("*", ""));
                    foreach (var dir in dirs)
                    {
                        var javaw = Path.Combine(dir, "bin", "javaw.exe");
                        var java = Path.Combine(dir, "bin", "java.exe");

                        if (File.Exists(javaw))
                        {
                            System.Diagnostics.Debug.WriteLine($"找到JDK Java: {javaw}");
                            return javaw;
                        }
                        if (File.Exists(java))
                        {
                            System.Diagnostics.Debug.WriteLine($"找到JDK Java: {java}");
                            return java;
                        }
                    }
                }
            }
            catch { }
        }

        System.Diagnostics.Debug.WriteLine("未找到Java");
        return null;
    }

    /// <summary>
    /// 释放资源，清理事件订阅
    /// </summary>
    public void Dispose()
    {
        OnLaunchOutput = null;
        OnLaunchError = null;
        OnEnvironmentCheck = null;
        _gameProcess?.Dispose();
        _gameProcess = null;
    }
}
