using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

public interface ILocalKnowledgeService
{
    List<string> Search(string topic, string query, int topN = 3);
    string GetChanAnalysisTemplate();
}

public class LocalKnowledgeService : ILocalKnowledgeService
{
    private readonly ILogger<LocalKnowledgeService> _logger;
    private readonly string _docsRoot;
    private readonly Lazy<List<string>> _chanDocuments;

    public LocalKnowledgeService(ILogger<LocalKnowledgeService> logger)
    {
        _logger = logger;
        _docsRoot = ResolveDocsRoot();
        _chanDocuments = new Lazy<List<string>>(LoadChanDocuments);
    }

    public List<string> Search(string topic, string query, int topN = 3)
    {
        if (!string.Equals(topic, "chan", StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        var docs = _chanDocuments.Value;
        if (docs.Count == 0)
            return new List<string>();

        var sections = docs
            .SelectMany(doc => Regex.Split(doc, @"(?=^##\s+)", RegexOptions.Multiline))
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 12)
            .ToList();

        var keywords = Regex.Matches(query ?? "", @"[\u4e00-\u9fa5A-Za-z0-9]+")
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = sections
            .Select(section => new
            {
                Section = section.Trim(),
                Score = keywords.Sum(k =>
                    Regex.Matches(section, Regex.Escape(k), RegexOptions.IgnoreCase).Count)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Section.Length > 0)
            .Where(x => x.Score > 0)
            .Take(Math.Max(1, topN))
            .Select(x => x.Section)
            .ToList();

        if (ranked.Count > 0)
            return ranked;

        return sections.Take(Math.Max(1, topN)).ToList();
    }

    public string GetChanAnalysisTemplate()
    {
        return """
            缠论分析模板：
            1. 先确认分析级别（日线/周线/月线）与观察区间。
            2. 判断最近结构中是否存在明确分型、笔、线段。
            3. 识别是否形成中枢，以及中枢扩展/离开方式。
            4. 观察是否出现背驰迹象。
            5. 最后判断买卖点类型，并说明该判断的不确定性。
            6. 结论必须回到当前K线数据本身，避免空泛定义堆砌。
            """;
    }

    private List<string> LoadChanDocuments()
    {
        try
        {
            var files = Directory.Exists(_docsRoot)
                ? Directory.GetFiles(_docsRoot, "chan_theory*.md", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            if (files.Count == 0)
            {
                _logger.LogWarning("未找到缠论知识库文件，目录: {Path}", _docsRoot);
                return new List<string>();
            }

            return files
                .Where(File.Exists)
                .Select(File.ReadAllText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载缠论知识库失败");
            return new List<string>();
        }
    }

    private static string ResolveDocsRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir is not null)
        {
            var docs = Path.Combine(dir.FullName, "docs");
            if (Directory.Exists(docs))
                return docs;
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "docs");
    }
}
