namespace InvestAgent.Core.Configuration;

/// <summary>
/// 应用程序全局配置选项。
/// 涵盖 AI 模型连接、数据源选择、第三方 API 密钥以及 Agent 行为参数。
/// 在 appsettings.json 中以 "AI" 节点配置，环境变量优先级更高。
/// </summary>
public class AgentOptions
{
    /// <summary>配置节名称（在 appsettings.json 中）</summary>
    public const string SectionName = "AI";

    /// <summary>LLM API 密钥（OpenAI 兼容）</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>LLM API 端点地址（OpenAI 兼容）</summary>
    public string Endpoint { get; set; } = "https://yunwu.ai/v1";

    /// <summary>使用的模型 ID</summary>
    public string ModelId { get; set; } = "gpt-4o";

    /// <summary>HTTP 代理地址（可选，如 http://127.0.0.1:7890）</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// 数据源选择："composite"（推荐，A股优先+Yahoo+AlphaVantage）、
    /// "yahoo"（纯 Yahoo Finance）、"eastmoney"（纯东方财富）
    /// </summary>
    public string DataSource { get; set; } = "composite";

    /// <summary>Yahoo Finance API 密钥</summary>
    public string YahooApiKey { get; set; } = "";

    /// <summary>是否启用 Yahoo Finance 数据源</summary>
    public bool YahooEnabled { get; set; } = true;

    /// <summary>Alpha Vantage API 密钥（用于新闻与情绪数据）</summary>
    public string AlphaVantageApiKey { get; set; } = "";

    /// <summary>是否启用 Alpha Vantage 数据源</summary>
    public bool AlphaVantageEnabled { get; set; } = true;

    /// <summary>Finnhub API 密钥（用于资金流近似数据）</summary>
    public string FinnhubApiKey { get; set; } = "";

    /// <summary>是否启用 Finnhub 数据源</summary>
    public bool FinnhubEnabled { get; set; } = true;

    /// <summary>Agent 最大执行步数（防止无限循环），默认 10</summary>
    public int MaxSteps { get; set; } = 10;

    /// <summary>最大对话轮次（超过后自动截断历史），默认 20</summary>
    public int MaxConversationTurns { get; set; } = 20;
}
