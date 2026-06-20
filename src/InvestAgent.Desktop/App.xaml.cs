using InvestAgent.Core.Configuration;
using InvestAgent.Core.Extensions;
using InvestAgent.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.IO;

namespace InvestAgent.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoadDotEnv();

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

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
                services.AddSingleton(options);
                services.AddInvestAgent(options);
                services.AddSingleton<IAnalysisHistoryRepository, AnalysisHistoryRepository>();
                services.AddSingleton<ViewModels.MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

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
