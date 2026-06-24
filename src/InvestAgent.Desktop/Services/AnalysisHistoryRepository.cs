using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace InvestAgent.Desktop.Services;

public interface IAnalysisHistoryRepository
{
    Task<long> SaveSessionAsync(PersistedAnalysisSession session);
    Task<List<SessionHistoryGroup>> ListSessionGroupsAsync();
    Task<PersistedAnalysisSession?> GetSessionAsync(long sessionId);
    Task DeleteSessionAsync(long sessionId);
}

/// <summary>
/// 基于 SQLite 的分析历史持久化实现。
/// 数据库文件存放于 %LocalAppData%/InvestAgent/analysis_history.db
/// </summary>
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
        MigrateLegacyHistoryIfNeeded();
    }

    public async Task<long> SaveSessionAsync(PersistedAnalysisSession session)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var sessionId = session.Record.Id;
        if (sessionId == 0)
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
INSERT INTO analysis_sessions(symbol, stock_name, session_title, created_at, updated_at)
VALUES($symbol, $stockName, $sessionTitle, $createdAt, $updatedAt);
SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$symbol", session.Record.Symbol);
            insert.Parameters.AddWithValue("$stockName", session.Record.StockName);
            insert.Parameters.AddWithValue("$sessionTitle", session.Record.SessionTitle);
            insert.Parameters.AddWithValue("$createdAt", session.Record.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", session.Record.UpdatedAt.ToString("O"));
            sessionId = Convert.ToInt64(await insert.ExecuteScalarAsync());
        }
        else
        {
            await using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"
UPDATE analysis_sessions
SET symbol = $symbol,
    stock_name = $stockName,
    session_title = $sessionTitle,
    updated_at = $updatedAt
WHERE id = $id;";
            update.Parameters.AddWithValue("$id", sessionId);
            update.Parameters.AddWithValue("$symbol", session.Record.Symbol);
            update.Parameters.AddWithValue("$stockName", session.Record.StockName);
            update.Parameters.AddWithValue("$sessionTitle", session.Record.SessionTitle);
            update.Parameters.AddWithValue("$updatedAt", session.Record.UpdatedAt.ToString("O"));
            await update.ExecuteNonQueryAsync();
        }

        session.Record.Id = sessionId;
        session.State.SessionId = sessionId;

        await DeleteChildrenAsync(conn, tx, sessionId);
        await InsertMessagesAsync(conn, tx, sessionId, session.Messages);
        await InsertWorkflowRunsAsync(conn, tx, sessionId, session.WorkflowRuns);
        await UpsertSnapshotAsync(conn, tx, sessionId, session.State);

        tx.Commit();
        return sessionId;
    }

    public async Task<List<SessionHistoryGroup>> ListSessionGroupsAsync()
    {
        var sessions = new List<AnalysisSessionRecord>();
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, symbol, stock_name, session_title, created_at, updated_at
FROM analysis_sessions
ORDER BY updated_at DESC, id DESC;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new AnalysisSessionRecord
            {
                Id = reader.GetInt64(0),
                Symbol = reader.IsDBNull(1) ? "" : reader.GetString(1),
                StockName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                SessionTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAt = ParseDate(reader.IsDBNull(4) ? "" : reader.GetString(4)),
                UpdatedAt = ParseDate(reader.IsDBNull(5) ? "" : reader.GetString(5))
            });
        }

        return sessions
            .GroupBy(x => $"{x.Symbol}|{x.StockName}")
            .Select(group => new SessionHistoryGroup
            {
                Symbol = group.First().Symbol,
                StockName = group.First().StockName,
                Sessions = group.OrderByDescending(x => x.UpdatedAt).ToList()
            })
            .OrderByDescending(x => x.Sessions.FirstOrDefault()?.UpdatedAt ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<PersistedAnalysisSession?> GetSessionAsync(long sessionId)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync();

        var record = await GetSessionRecordAsync(conn, sessionId);
        if (record is null) return null;

        var state = await GetSnapshotAsync(conn, sessionId) ?? new AnalysisSessionState
        {
            SessionId = sessionId,
            Symbol = record.Symbol,
            StockName = record.StockName,
            SessionTitle = record.SessionTitle,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };

        var messages = await GetMessagesAsync(conn, sessionId);
        var workflowRuns = await GetWorkflowRunsAsync(conn, sessionId);

        return new PersistedAnalysisSession
        {
            Record = record,
            State = state,
            Messages = messages,
            WorkflowRuns = workflowRuns
        };
    }

    public async Task DeleteSessionAsync(long sessionId)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        await DeleteChildrenAsync(conn, tx, sessionId);

        await using var deleteSession = conn.CreateCommand();
        deleteSession.Transaction = tx;
        deleteSession.CommandText = "DELETE FROM analysis_sessions WHERE id = $sessionId;";
        deleteSession.Parameters.AddWithValue("$sessionId", sessionId);
        await deleteSession.ExecuteNonQueryAsync();

        tx.Commit();
    }

    private SqliteConnection OpenConnection() => new($"Data Source={_dbPath}");

    private void EnsureDatabase()
    {
        using var conn = OpenConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS analysis_sessions(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    stock_name TEXT NOT NULL,
    session_title TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS analysis_messages(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL,
    role TEXT NOT NULL,
    content TEXT NOT NULL,
    created_at TEXT NOT NULL,
    turn_index INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS analysis_workflow_runs(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL,
    trigger_turn_index INTEGER NOT NULL,
    agent_name TEXT NOT NULL,
    steps_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS analysis_session_snapshots(
    session_id INTEGER PRIMARY KEY,
    session_state_json TEXT NOT NULL,
    last_final_response TEXT NOT NULL,
    last_updated_at TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();
    }

    private void MigrateLegacyHistoryIfNeeded()
    {
        using var conn = OpenConnection();
        conn.Open();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(1) FROM analysis_sessions;";
        var sessionCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        if (sessionCount > 0) return;

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='analysis_history';";
        var hasLegacyTable = checkCmd.ExecuteScalar() is not null;
        if (!hasLegacyTable) return;

        using var select = conn.CreateCommand();
        select.CommandText = @"
SELECT symbol, stock_name, main_business, final_response, metrics_summary, agent_b, agent_c, agent_d, created_at,
       daily_klines_json, monthly_klines_json, company_news_json, industry_news_json, financial_history_json, workflow_steps_json
FROM analysis_history;";

        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var symbol = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var stockName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var mainBusiness = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var finalResponse = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var createdAt = ParseDate(reader.IsDBNull(8) ? "" : reader.GetString(8));

            var sessionState = new AnalysisSessionState
            {
                Symbol = symbol,
                StockName = stockName,
                SessionTitle = $"初始分析 {createdAt:yyyy-MM-dd HH:mm}",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                MainBusiness = mainBusiness,
                FinalResponse = finalResponse,
                AgentBResult = reader.IsDBNull(5) ? "" : reader.GetString(5),
                AgentCResult = reader.IsDBNull(6) ? "" : reader.GetString(6),
                AgentDResult = reader.IsDBNull(7) ? "" : reader.GetString(7),
                DailyKLines = DeserializeList<StockKLine>(reader.IsDBNull(9) ? "[]" : reader.GetString(9)),
                MonthlyKLines = DeserializeList<StockKLine>(reader.IsDBNull(10) ? "[]" : reader.GetString(10)),
                CompanyNews = DeserializeList<NewsItem>(reader.IsDBNull(11) ? "[]" : reader.GetString(11)),
                IndustryNews = DeserializeList<NewsItem>(reader.IsDBNull(12) ? "[]" : reader.GetString(12)),
                FinancialHistory = DeserializeList<KeyMetrics>(reader.IsDBNull(13) ? "[]" : reader.GetString(13))
            };

            var workflowSteps = DeserializeList<AgentStep>(reader.IsDBNull(14) ? "[]" : reader.GetString(14));
            var persisted = new PersistedAnalysisSession
            {
                Record = new AnalysisSessionRecord
                {
                    Symbol = symbol,
                    StockName = stockName,
                    SessionTitle = sessionState.SessionTitle,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                },
                State = sessionState,
                Messages = new List<SessionChatMessage>
                {
                    new() { Role = "user", Content = string.IsNullOrWhiteSpace(stockName) ? symbol : $"{symbol} {stockName}", CreatedAt = createdAt, TurnIndex = 1 },
                    new() { Role = "assistant", Content = finalResponse, CreatedAt = createdAt, TurnIndex = 1 }
                },
                WorkflowRuns = workflowSteps.Count == 0
                    ? new List<WorkflowRunRecord>()
                    : new List<WorkflowRunRecord>
                    {
                        new()
                        {
                            AgentName = "Legacy",
                            TriggerTurnIndex = 1,
                            CreatedAt = createdAt,
                            Steps = workflowSteps
                        }
                    }
            };

            SaveSessionSync(conn, persisted);
        }
    }

    private void SaveSessionSync(SqliteConnection conn, PersistedAnalysisSession session)
    {
        using var tx = conn.BeginTransaction();
        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
INSERT INTO analysis_sessions(symbol, stock_name, session_title, created_at, updated_at)
VALUES($symbol, $stockName, $sessionTitle, $createdAt, $updatedAt);
SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$symbol", session.Record.Symbol);
        insert.Parameters.AddWithValue("$stockName", session.Record.StockName);
        insert.Parameters.AddWithValue("$sessionTitle", session.Record.SessionTitle);
        insert.Parameters.AddWithValue("$createdAt", session.Record.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$updatedAt", session.Record.UpdatedAt.ToString("O"));
        var sessionId = Convert.ToInt64(insert.ExecuteScalar());
        session.Record.Id = sessionId;
        session.State.SessionId = sessionId;

        InsertMessagesSync(conn, tx, sessionId, session.Messages);
        InsertWorkflowRunsSync(conn, tx, sessionId, session.WorkflowRuns);
        UpsertSnapshotSync(conn, tx, sessionId, session.State);
        tx.Commit();
    }

    private async Task DeleteChildrenAsync(SqliteConnection conn, SqliteTransaction tx, long sessionId)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM analysis_messages WHERE session_id = $sessionId;",
                     "DELETE FROM analysis_workflow_runs WHERE session_id = $sessionId;",
                     "DELETE FROM analysis_session_snapshots WHERE session_id = $sessionId;"
                 })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertMessagesAsync(SqliteConnection conn, SqliteTransaction tx, long sessionId, List<SessionChatMessage> messages)
    {
        foreach (var message in messages)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO analysis_messages(session_id, role, content, created_at, turn_index)
VALUES($sessionId, $role, $content, $createdAt, $turnIndex);";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$role", message.Role);
            cmd.Parameters.AddWithValue("$content", message.Content);
            cmd.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$turnIndex", message.TurnIndex);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertWorkflowRunsAsync(SqliteConnection conn, SqliteTransaction tx, long sessionId, List<WorkflowRunRecord> workflowRuns)
    {
        foreach (var run in workflowRuns)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO analysis_workflow_runs(session_id, trigger_turn_index, agent_name, steps_json, created_at)
VALUES($sessionId, $turnIndex, $agentName, $stepsJson, $createdAt);";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$turnIndex", run.TriggerTurnIndex);
            cmd.Parameters.AddWithValue("$agentName", run.AgentName);
            cmd.Parameters.AddWithValue("$stepsJson", JsonSerializer.Serialize(run.Steps, _jsonOptions));
            cmd.Parameters.AddWithValue("$createdAt", run.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task UpsertSnapshotAsync(SqliteConnection conn, SqliteTransaction tx, long sessionId, AnalysisSessionState state)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO analysis_session_snapshots(session_id, session_state_json, last_final_response, last_updated_at)
VALUES($sessionId, $stateJson, $finalResponse, $updatedAt);";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$stateJson", JsonSerializer.Serialize(state, _jsonOptions));
        cmd.Parameters.AddWithValue("$finalResponse", state.FinalResponse);
        cmd.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private void InsertMessagesSync(SqliteConnection conn, SqliteTransaction tx, long sessionId, List<SessionChatMessage> messages)
    {
        foreach (var message in messages)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO analysis_messages(session_id, role, content, created_at, turn_index)
VALUES($sessionId, $role, $content, $createdAt, $turnIndex);";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$role", message.Role);
            cmd.Parameters.AddWithValue("$content", message.Content);
            cmd.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$turnIndex", message.TurnIndex);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertWorkflowRunsSync(SqliteConnection conn, SqliteTransaction tx, long sessionId, List<WorkflowRunRecord> workflowRuns)
    {
        foreach (var run in workflowRuns)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO analysis_workflow_runs(session_id, trigger_turn_index, agent_name, steps_json, created_at)
VALUES($sessionId, $turnIndex, $agentName, $stepsJson, $createdAt);";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$turnIndex", run.TriggerTurnIndex);
            cmd.Parameters.AddWithValue("$agentName", run.AgentName);
            cmd.Parameters.AddWithValue("$stepsJson", JsonSerializer.Serialize(run.Steps, _jsonOptions));
            cmd.Parameters.AddWithValue("$createdAt", run.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private void UpsertSnapshotSync(SqliteConnection conn, SqliteTransaction tx, long sessionId, AnalysisSessionState state)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO analysis_session_snapshots(session_id, session_state_json, last_final_response, last_updated_at)
VALUES($sessionId, $stateJson, $finalResponse, $updatedAt);";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$stateJson", JsonSerializer.Serialize(state, _jsonOptions));
        cmd.Parameters.AddWithValue("$finalResponse", state.FinalResponse);
        cmd.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private async Task<AnalysisSessionRecord?> GetSessionRecordAsync(SqliteConnection conn, long sessionId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, symbol, stock_name, session_title, created_at, updated_at
FROM analysis_sessions
WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new AnalysisSessionRecord
        {
            Id = reader.GetInt64(0),
            Symbol = reader.IsDBNull(1) ? "" : reader.GetString(1),
            StockName = reader.IsDBNull(2) ? "" : reader.GetString(2),
            SessionTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
            CreatedAt = ParseDate(reader.IsDBNull(4) ? "" : reader.GetString(4)),
            UpdatedAt = ParseDate(reader.IsDBNull(5) ? "" : reader.GetString(5))
        };
    }

    private async Task<AnalysisSessionState?> GetSnapshotAsync(SqliteConnection conn, long sessionId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT session_state_json
FROM analysis_session_snapshots
WHERE session_id = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        var json = await cmd.ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AnalysisSessionState>(json, _jsonOptions);
    }

    private async Task<List<SessionChatMessage>> GetMessagesAsync(SqliteConnection conn, long sessionId)
    {
        var messages = new List<SessionChatMessage>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, role, content, created_at, turn_index
FROM analysis_messages
WHERE session_id = $sessionId
ORDER BY turn_index, id;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new SessionChatMessage
            {
                Id = reader.GetInt64(0),
                Role = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Content = reader.IsDBNull(2) ? "" : reader.GetString(2),
                CreatedAt = ParseDate(reader.IsDBNull(3) ? "" : reader.GetString(3)),
                TurnIndex = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
        }
        return messages;
    }

    private async Task<List<WorkflowRunRecord>> GetWorkflowRunsAsync(SqliteConnection conn, long sessionId)
    {
        var runs = new List<WorkflowRunRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, trigger_turn_index, agent_name, steps_json, created_at
FROM analysis_workflow_runs
WHERE session_id = $sessionId
ORDER BY id;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(new WorkflowRunRecord
            {
                Id = reader.GetInt64(0),
                TriggerTurnIndex = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                AgentName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Steps = DeserializeList<AgentStep>(reader.IsDBNull(3) ? "[]" : reader.GetString(3)),
                CreatedAt = ParseDate(reader.IsDBNull(4) ? "" : reader.GetString(4))
            });
        }
        return runs;
    }

    private List<T> DeserializeList<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static DateTime ParseDate(string raw)
    {
        return DateTime.TryParse(raw, out var dt) ? dt : DateTime.MinValue;
    }
}
