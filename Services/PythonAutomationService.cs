using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ourmclauncher.Services;

/// <summary>
/// Python自动化脚本服务 - 支持Python自动化任务
/// </summary>
public class PythonAutomationService
{
    private string? _pythonPath;
    private readonly string _scriptsDir;

    public PythonAutomationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var omlDir = Path.Combine(appData, ".minecraft", "oml");
        _scriptsDir = Path.Combine(omlDir, "scripts");
        Directory.CreateDirectory(_scriptsDir);
    }

    /// <summary>
    /// 检测Python环境
    /// </summary>
    public async Task<(bool found, string? path, string version)> DetectPythonAsync()
    {
        var pythonCommands = new[] { "python3", "python", "py" };

        foreach (var command in pythonCommands)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    continue;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                var output = (await outputTask).Trim();
                var error = (await errorTask).Trim();
                var versionOutput = string.IsNullOrEmpty(output) ? error : output;

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(versionOutput))
                {
                    _pythonPath = command;
                    return (true, command, versionOutput);
                }
            }
            catch { }
        }

        return (false, null, "");
    }

    /// <summary>
    /// 执行Python脚本
    /// </summary>
    public async Task<PythonScriptResult> ExecuteScriptAsync(string scriptName, string args = "")
    {
        var result = new PythonScriptResult { Success = false };

        if (string.IsNullOrEmpty(_pythonPath))
        {
            var (found, path, _) = await DetectPythonAsync();
            if (!found)
            {
                result.Error = "未找到Python环境";
                return result;
            }
        }

        try
        {
            if (string.IsNullOrWhiteSpace(scriptName) ||
                !string.Equals(Path.GetFileName(scriptName), scriptName, StringComparison.Ordinal))
            {
                result.Error = "脚本名称无效";
                return result;
            }

            var scriptPath = Path.Combine(_scriptsDir, $"{scriptName}.py");

            if (!File.Exists(scriptPath))
            {
                result.Error = $"脚本不存在: {scriptName}";
                return result;
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath ?? "python",
                Arguments = $"\"{scriptPath}\" {args}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                result.Error = "无法启动 Python 进程";
                return result;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            result.Success = process.ExitCode == 0;
            result.Output = output;
            result.Error = error;
            result.ExitCode = process.ExitCode;

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 创建自动化脚本
    /// </summary>
    public void CreateDefaultScripts()
    {
        // 在这里创建Python脚本文件
        // 由于Python脚本的复杂性，建议将脚本保存为单独的.py文件
        // 然后通过ExecuteScriptAsync调用

        System.Diagnostics.Debug.WriteLine("Python脚本已准备好在首次使用时创建");
    }

    /// <summary>
    /// 执行Java自动检测
    /// </summary>
    public async Task<string> AutoDetectJavaAsync()
    {
        var result = await ExecuteScriptAsync("detect_java");
        if (result.Success && !string.IsNullOrEmpty(result.Output))
        {
            try
            {
                var javaVersions = JsonSerializer.Deserialize<List<JavaInfo>>(result.Output);
                if (javaVersions != null && javaVersions.Count > 0)
                {
                    return javaVersions[0].Path ?? "";
                }
            }
            catch { }
        }
        return "";
    }

    /// <summary>
    /// 执行缓存清理
    /// </summary>
    public async Task<string> CleanCacheAsync()
    {
        var result = await ExecuteScriptAsync("clean_cache");
        return result.Output ?? "";
    }

    /// <summary>
    /// 获取脚本目录
    /// </summary>
    public string GetScriptsDir()
    {
        return _scriptsDir;
    }
}

/// <summary>
/// Python脚本执行结果
/// </summary>
public class PythonScriptResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public int ExitCode { get; set; }
}

/// <summary>
/// Java信息
/// </summary>
public class JavaInfo
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
}
