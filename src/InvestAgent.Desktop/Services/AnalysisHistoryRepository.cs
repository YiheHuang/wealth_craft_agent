using System.Text.Json;
using System.IO;
using InvestAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace InvestAgent.Desktop.Services;

public record AnalysisHistoryListItem(long Id, string Symbol, string StockName, DateTime CreatedAt)
{
    public string DisplayText => $"{CreatedAt:yyyy-MM-dd HH:mm} | {Symbol} {StockName}";
}

public class AnalysisHistoryRecord
{
    public long Id { get; set; }
    public string Symbol { get; set; } = "";
    public string StockName { get; set; } = "";
    public string MainBusiness { get; set; } = "";
    public string FinalResponse { get; set; } = "";
    public string MetricsSummary { get; set; } = "";
    public string AgentB { get; set; } = "";
    public string AgentC { get; set; } = "";
    public string AgentD { get; set; } = "";
    public List<HistoryStepItem> WorkflowSteps { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public List<StockKLine> DailyKLines { get; set; } = new();
    public List<StockKLine> MonthlyKLines { get; set; } = new();
    public List<NewsItem> CompanyNews { get; set; } = new();
    public List<NewsItem> IndustryNews { get; set; } = new();
    public List<KeyMetrics> FinancialHistory { get; set; } = new();
}

public class HistoryStepItem
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

public interface IAnalysisHistoryRepository
{
    Task SaveAsync(AnalysisHistoryRecord record);
    Task<List<AnalysisHistoryListItem>> ListAsync(int top = 200);
    Task<AnalysisHistoryRecord?> GetAsync(long id);
}

public class AnalysisHistoryRepository : IAnalysisHistoryRepository
{
    private readonly string _dbPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AnalysisHistoryRepository()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvestAgent");
        Directory.CreateDirectory(baseDir);
        _dbPath = Path.Combine(baseDir, "analysis_history.db");
        EnsureDatabase();
    }

    public async Task SaveAsync(AnalysisHistoryRecord record)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        // 同一股票仅保留最新一条记录：新分析覆盖旧记录
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM analysis_history WHERE symbol = $symbol;";
            del.Parameters.AddWithValue("$symbol", record.Symbol);
            await del.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO analysis_history(symbol, stock_name, main_business, final_response, metrics_summary, agent_b, agent_c, agent_d, created_at,
daily_klines_json, monthly_klines_json, company_news_json, industry_news_json, financial_history_json, workflow_steps_json)
VALUES($symbol,$stockName,$mainBusiness,$finalResponse,$metricsSummary,$agentB,$agentC,$agentD,$createdAt,
$daily,$monthly,$companyNews,$industryNews,$financialHistory,$workflowSteps);";
        cmd.Parameters.AddWithValue("$symbol", record.Symbol);
        cmd.Parameters.AddWithValue("$stockName", record.StockName);
        cmd.Parameters.AddWithValue("$mainBusiness", record.MainBusiness);
        cmd.Parameters.AddWithValue("$finalResponse", record.FinalResponse);
        cmd.Parameters.AddWithValue("$metricsSummary", record.MetricsSummary);
        cmd.Parameters.AddWithValue("$agentB", record.AgentB);
        cmd.Parameters.AddWithValue("$agentC", record.AgentC);
        cmd.Parameters.AddWithValue("$agentD", record.AgentD);
        cmd.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$daily", JsonSerializer.Serialize(record.DailyKLines, _jsonOptions));
        cmd.Parameters.AddWithValue("$monthly", JsonSerializer.Serialize(record.MonthlyKLines, _jsonOptions));
        cmd.Parameters.AddWithValue("$companyNews", JsonSerializer.Serialize(record.CompanyNews, _jsonOptions));
        cmd.Parameters.AddWithValue("$industryNews", JsonSerializer.Serialize(record.IndustryNews, _jsonOptions));
        cmd.Parameters.AddWithValue("$financialHistory", JsonSerializer.Serialize(record.FinancialHistory, _jsonOptions));
        cmd.Parameters.AddWithValue("$workflowSteps", JsonSerializer.Serialize(record.WorkflowSteps, _jsonOptions));
        await cmd.ExecuteNonQueryAsync();
        tx.Commit();
    }

    public async Task<List<AnalysisHistoryListItem>> ListAsync(int top = 200)
    {
        var list = new List<AnalysisHistoryListItem>();
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, symbol, stock_name, created_at
FROM analysis_history
ORDER BY id DESC
LIMIT $top;";
        cmd.Parameters.AddWithValue("$top", Math.Max(1, top));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            var symbol = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var stockName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var createdAtText = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var createdAt = DateTime.TryParse(createdAtText, out var dt) ? dt : DateTime.MinValue;
            list.Add(new AnalysisHistoryListItem(id, symbol, stockName, createdAt));
        }
        return list;
    }

    public async Task<AnalysisHistoryRecord?> GetAsync(long id)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, symbol, stock_name, main_business, final_response, metrics_summary, agent_b, agent_c, agent_d, created_at,
daily_klines_json, monthly_klines_json, company_news_json, industry_news_json, financial_history_json, workflow_steps_json
FROM analysis_history
WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var r = new AnalysisHistoryRecord
        {
            Id = reader.GetInt64(0),
            Symbol = reader.IsDBNull(1) ? "" : reader.GetString(1),
            StockName = reader.IsDBNull(2) ? "" : reader.GetString(2),
            MainBusiness = reader.IsDBNull(3) ? "" : reader.GetString(3),
            FinalResponse = reader.IsDBNull(4) ? "" : reader.GetString(4),
            MetricsSummary = reader.IsDBNull(5) ? "" : reader.GetString(5),
            AgentB = reader.IsDBNull(6) ? "" : reader.GetString(6),
            AgentC = reader.IsDBNull(7) ? "" : reader.GetString(7),
            AgentD = reader.IsDBNull(8) ? "" : reader.GetString(8),
            CreatedAt = DateTime.TryParse(reader.IsDBNull(9) ? "" : reader.GetString(9), out var dt) ? dt : DateTime.MinValue,
            DailyKLines = DeserializeList<StockKLine>(reader.IsDBNull(10) ? "[]" : reader.GetString(10)),
            MonthlyKLines = DeserializeList<StockKLine>(reader.IsDBNull(11) ? "[]" : reader.GetString(11)),
            CompanyNews = DeserializeList<NewsItem>(reader.IsDBNull(12) ? "[]" : reader.GetString(12)),
            IndustryNews = DeserializeList<NewsItem>(reader.IsDBNull(13) ? "[]" : reader.GetString(13)),
            FinancialHistory = DeserializeList<KeyMetrics>(reader.IsDBNull(14) ? "[]" : reader.GetString(14)),
            WorkflowSteps = DeserializeList<HistoryStepItem>(reader.IsDBNull(15) ? "[]" : reader.GetString(15))
        };
        return r;
    }

    private SqliteConnection OpenConnection() => new($"Data Source={_dbPath}");

    private void EnsureDatabase()
    {
        using var conn = OpenConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS analysis_history(
id INTEGER PRIMARY KEY AUTOINCREMENT,
symbol TEXT NOT NULL,
stock_name TEXT NOT NULL,
main_business TEXT NOT NULL,
final_response TEXT NOT NULL,
metrics_summary TEXT NOT NULL,
agent_b TEXT NOT NULL,
agent_c TEXT NOT NULL,
agent_d TEXT NOT NULL,
created_at TEXT NOT NULL,
daily_klines_json TEXT NOT NULL,
monthly_klines_json TEXT NOT NULL,
company_news_json TEXT NOT NULL,
industry_news_json TEXT NOT NULL,
financial_history_json TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();

        using var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE analysis_history ADD COLUMN workflow_steps_json TEXT NOT NULL DEFAULT '[]';";
        try { alter.ExecuteNonQuery(); } catch { }
        using var alter2 = conn.CreateCommand();
        alter2.CommandText = "ALTER TABLE analysis_history ADD COLUMN stock_name TEXT NOT NULL DEFAULT '';";
        try { alter2.ExecuteNonQuery(); } catch { }
    }

    private List<T> DeserializeList<T>(string json)
    {
        try { return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>(); }
        catch { return new List<T>(); }
    }
}
