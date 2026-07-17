using System.Reflection;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using ourmclauncher.Services;

namespace ourmclauncher;

/// <summary>
/// 程序入口点
/// </summary>
internal static class Program
{
    /// <summary>
    /// 从嵌入资源中提取 wwwroot 文件到临时目录，返回 index.html 的完整路径。
    /// 兼容单文件发布（EmbeddedResource）与常规发布两种场景。
    /// </summary>
    private static string ExtractAndGetHostPage()
    {
        const string resourcePrefix = "WWWROOT\\";
        var assembly = Assembly.GetExecutingAssembly();
        var extractDir = Path.Combine(Path.GetTempPath(), "ourmclauncher", "wwwroot");

        // 如果已解压且 index.html 存在，直接返回（避免重复解压）
        var existingIndex = Path.Combine(extractDir, "index.html");
        if (File.Exists(existingIndex))
            return existingIndex;

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix))
                continue;

            // LogicalName = "WWWROOT\css\app.css" 或 "WWWROOT\index.html"
            var relativePath = resourceName[resourcePrefix.Length..];
            var targetFile = Path.Combine(extractDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var fileStream = File.Create(targetFile);
            stream.CopyTo(fileStream);
        }

        return Path.Combine(extractDir, "index.html");
    }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new LauncherForm();

        var windowService = new WindowService(form);
        var settingsService = new SettingsService();
        var pathService = new PathService(settingsService);
        var versionService = new VersionService(pathService);
        var environmentService = new EnvironmentService(pathService, settingsService);
        var launchService = new LaunchService(settingsService, pathService, environmentService);

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<AppState>();
        services.AddSingleton<SkinService>();
        services.AddSingleton<DownloadService>(sp => new DownloadService(pathService));
        services.AddSingleton<WindowService>(windowService);
        services.AddSingleton<SettingsService>(settingsService);
        services.AddSingleton<PathService>(pathService);
        services.AddSingleton<VersionService>(versionService);
        services.AddSingleton<LaunchService>(launchService);
        services.AddSingleton<NotificationService>();
        services.AddSingleton<LogService>();
        services.AddSingleton<ModService>();
        services.AddSingleton<AccountManagerService>();
        services.AddSingleton<MicrosoftAuthService>();
        services.AddSingleton<LaunchPresetService>();
        services.AddSingleton<InstanceService>(sp => new InstanceService(settingsService));
        services.AddSingleton<ModRepositoryService>();
        services.AddSingleton<ResourceService>(sp => new ResourceService(pathService));
        services.AddSingleton<PythonAutomationService>();
        services.AddSingleton<VersionManagementService>(sp => new VersionManagementService(pathService));
        services.AddSingleton<ModManagementService>(sp => new ModManagementService(pathService));
        services.AddSingleton<PerformanceService>();
        services.AddSingleton<EnvironmentService>(sp => new EnvironmentService(pathService, settingsService));
        services.AddSingleton<AIService>(sp => new AIService(settingsService));

        var blazor = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = ExtractAndGetHostPage(),
            Services = services.BuildServiceProvider()
        };

        blazor.RootComponents.Add<Main>("#app");

        // WebView2 加载完成后使用淡入动画显示窗口
        blazor.WebView.NavigationCompleted += (s, e) =>
        {
            form.BeginInvoke(new Action(() =>
            {
                form.FadeIn();
            }));
        };

        form.Controls.Add(blazor);

        Application.Run(form);
    }
}
