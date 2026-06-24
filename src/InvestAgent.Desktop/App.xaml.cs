using InvestAgent.Core.Configuration;
using InvestAgent.Core.Extensions;
using InvestAgent.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.IO;

namespace InvestAgent.Desktop;

/// <summary>
/// WPF 桌面应用入口点。
/// 负责应用程序生命周期管理——启动时加载 .env 环境变量、
/// 构建 DI 容器（IHost）、初始化 MainWindow 并显示。
/// 退出时优雅停止 Host。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// 应用启动回调。
    /// 按顺序执行：加载 .env → 构建配置 → 注册 DI → 显示主窗口。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoadDotEnv();

        // 从环境变量构建配置（比硬编码更灵活）
        var options = new AgentOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? Environment.GetEnvironmentVariable("LLM_API_KEY")
                ?? "",
            Endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
                ?? Environment.GetEnvironmentVariable("LLM_BASE_URL")
                ?? "https://yunwu.ai/v1",
            ModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini",
            ProxyUrl = Environment.GetEnvironmentVariable("HTTP_PROXY")
                ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                ?? Environment.GetEnvironmentVariable("ALL_PROXY"),
            DataSource = Environment.GetEnvironmentVariable("INVEST_DATA_SOURCE") ?? "composite",
            AlphaVantageApiKey = Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY") ?? "",
            FinnhubApiKey = Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? ""
        };

        // 使用 .NET Generic Host 作为 DI 容器
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
                services.AddSingleton(options);
                services.AddInvestAgent(options);                              // 核心服务
                services.AddSingleton<IAnalysisHistoryRepository, AnalysisHistoryRepository>(); // SQLite 持久化
                services.AddSingleton<ViewModels.MainViewModel>();            // 主视图模型
                services.AddSingleton<MainWindow>();                          // 主窗口
            })
            .Build();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// 加载 .env 文件到环境变量。
    /// 按优先级搜索：当前目录 → 应用程序目录 → 向上遍历父目录。
    /// </summary>
    private static void LoadDotEnv()
    {
        var envPath = FindEnvFile();
        if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath)) return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

            var idx = trimmed.IndexOf('=');
            if (idx <= 0) continue;

            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(key))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>搜索 .env 文件——从多级目录向上查找</summary>
    private static string? FindEnvFile()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env")
        };
        foreach (var p in candidates)
            if (File.Exists(p)) return p;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, ".env");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>应用退出时优雅停止 Host（释放资源）</summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
