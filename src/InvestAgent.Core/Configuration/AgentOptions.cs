namespace InvestAgent.Core.Configuration;

public class AgentOptions
{
    public const string SectionName = "AI";
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://yunwu.ai/v1";
    public string ModelId { get; set; } = "gpt-4o";
    public string? ProxyUrl { get; set; }
    public string DataSource { get; set; } = "composite";  // "composite" | "yahoo" | "eastmoney"
    public string YahooApiKey { get; set; } = "";
    public bool YahooEnabled { get; set; } = true;
    public string AlphaVantageApiKey { get; set; } = "";
    public bool AlphaVantageEnabled { get; set; } = true;
    public string FinnhubApiKey { get; set; } = "";
    public bool FinnhubEnabled { get; set; } = true;
    public int MaxSteps { get; set; } = 10;
    public int MaxConversationTurns { get; set; } = 20;
}
