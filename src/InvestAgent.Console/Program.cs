using InvestAgent.Core.Configuration;
using InvestAgent.Core.Extensions;
using InvestAgent.Console.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// InvestAgent 控制台应用程序入口点。
/// 解析命令行参数，配置 DI 容器，启动终端交互界面。
///
/// 用法：dotnet run -- &lt;api-key&gt; [--proxy url] [--source name] [--alpha-key key] [--finnhub-key key]
/// </summary>

// ── 命令行参数解析 ────────────────────────────────────
var apiKey = "";
string? proxyUrl = null;
var dataSource = "composite";
var alphaVantageKey = Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY") ?? "";
var finnhubKey = Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? "";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--proxy" && i + 1 < args.Length)
        proxyUrl = args[++i];
    else if (args[i] == "--source" && i + 1 < args.Length)
        dataSource = args[++i];
    else if (args[i] == "--alpha-key" && i + 1 < args.Length)
        alphaVantageKey = args[++i];
    else if (args[i] == "--finnhub-key" && i + 1 < args.Length)
        finnhubKey = args[++i];
    else if (!args[i].StartsWith("--"))
        apiKey = args[i];
}

// ── 参数校验 ──────────────────────────────────────────
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("用法: dotnet run -- <api-key> [--proxy url] [--source name]");
    Console.WriteLine("  --proxy   HTTP 代理地址, 例如 http://127.0.0.1:7890");
    Console.WriteLine("  --source  数据源, composite(默认) | yahoo | eastmoney");
    Console.WriteLine("  --alpha-key    Alpha Vantage API Key");
    Console.WriteLine("  --finnhub-key  Finnhub API Key");
    return 1;
}

// ── 配置选项 ──────────────────────────────────────────
var options = new AgentOptions
{
    ApiKey = apiKey,
    Endpoint = "https://yunwu.ai/v1",
    ModelId = "gpt-4o-mini",
    ProxyUrl = proxyUrl,
    DataSource = dataSource,
    AlphaVantageApiKey = alphaVantageKey,
    FinnhubApiKey = finnhubKey
};

Console.WriteLine($"[数据源] {options.DataSource}");
if (!string.IsNullOrEmpty(options.ProxyUrl))
    Console.WriteLine($"[代理] {options.ProxyUrl}");

// ── DI 容器构建 ──────────────────────────────────────
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddSingleton(options);
services.AddInvestAgent(options);       // 注册所有核心服务
services.AddSingleton<ConsoleUI>();     // 注册终端 UI

var provider = services.BuildServiceProvider();

// ── 启动应用 ─────────────────────────────────────────
try
{
    var ui = provider.GetRequiredService<ConsoleUI>();
    await ui.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"启动失败: {ex.Message}");
    return 1;
}
