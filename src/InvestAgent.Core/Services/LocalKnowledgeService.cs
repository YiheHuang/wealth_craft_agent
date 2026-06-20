using System.Text.RegularExpressions;
using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

public interface ILocalKnowledgeService
{
    List<string> Search(string topic, string query, int topN = 3);
    List<ChanImageResource> SearchChanImages(string query, int topN = 6);
    string GetChanAnalysisTemplate();
}

public class LocalKnowledgeService : ILocalKnowledgeService
{
    private readonly ILogger<LocalKnowledgeService> _logger;
    private readonly string _docsRoot;
    private readonly Lazy<List<string>> _chanDocuments;
    private readonly Lazy<List<ChanImageResource>> _chanImages;

    public LocalKnowledgeService(ILogger<LocalKnowledgeService> logger)
    {
        _logger = logger;
        _docsRoot = ResolveDocsRoot();
        _chanDocuments = new Lazy<List<string>>(LoadChanDocuments);
        _chanImages = new Lazy<List<ChanImageResource>>(LoadChanImages);
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

    public List<ChanImageResource> SearchChanImages(string query, int topN = 6)
    {
        var images = _chanImages.Value;
        if (images.Count == 0)
            return new List<ChanImageResource>();

        var keywords = BuildImageSearchKeywords(query);
        if (keywords.Count == 0)
            keywords.Add("chart-example");

        return images
            .Select(image => new
            {
                Image = image,
                Score = ScoreImage(image, keywords)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Image.Collection == "illustrated" ? 0 : 1)
            .ThenBy(x => x.Image.ArticleNo ?? int.MaxValue)
            .ThenBy(x => x.Image.ImageIndex)
            .Take(Math.Max(1, topN))
            .Select(x => x.Image)
            .ToList();
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

    private List<ChanImageResource> LoadChanImages()
    {
        try
        {
            var manifestPath = Path.Combine(_docsRoot, "chan_images", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning("未找到缠论图片索引: {Path}", manifestPath);
                return new List<ChanImageResource>();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty("images", out var imagesElement) ||
                imagesElement.ValueKind != JsonValueKind.Array)
            {
                return new List<ChanImageResource>();
            }

            var result = new List<ChanImageResource>();
            foreach (var item in imagesElement.EnumerateArray())
            {
                result.Add(new ChanImageResource
                {
                    Id = GetString(item, "id"),
                    Collection = GetString(item, "collection"),
                    ArticleKey = GetString(item, "articleKey"),
                    ArticleNo = GetNullableInt(item, "articleNo"),
                    Title = GetString(item, "title"),
                    Date = GetString(item, "date"),
                    PageUrl = GetString(item, "pageUrl"),
                    ImageIndex = GetInt(item, "imageIndex"),
                    ImageUrl = GetString(item, "imageUrl"),
                    LocalPath = GetString(item, "localPath"),
                    OriginalFileName = GetString(item, "originalFileName"),
                    Alt = GetString(item, "alt"),
                    ContextBefore = GetString(item, "contextBefore"),
                    ContextAfter = GetString(item, "contextAfter"),
                    Tags = GetStringArray(item, "tags")
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载缠论图片索引失败");
            return new List<ChanImageResource>();
        }
    }

    private static List<string> BuildImageSearchKeywords(string query)
    {
        var raw = query ?? "";
        var keywords = Regex.Matches(raw, @"[\u4e00-\u9fa5A-Za-z0-9\-]+")
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 1)
            .ToList();

        void AddIfContains(string keyword, string tag)
        {
            if (raw.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                keywords.Add(tag);
        }

        AddIfContains("图", "chart-example");
        AddIfContains("图解", "chart-example");
        AddIfContains("案例", "chart-example");
        AddIfContains("缠论", "chart-example");
        AddIfContains("缠中说禅", "chart-example");
        AddIfContains("分型", "fractal");
        AddIfContains("笔", "bi");
        AddIfContains("线段", "segment");
        AddIfContains("中枢", "zhongshu");
        AddIfContains("背驰", "divergence");
        AddIfContains("背弛", "divergence");
        AddIfContains("买点", "buy-sell-point");
        AddIfContains("卖点", "buy-sell-point");
        AddIfContains("区间套", "interval-nesting");
        AddIfContains("同级别", "same-level-decomposition");
        AddIfContains("连接", "connection-composition");
        AddIfContains("走势", "trend-structure");
        AddIfContains("MACD", "macd");

        var distinct = keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0)
            distinct.Add("chart-example");

        return distinct;
    }

    private static int ScoreImage(ChanImageResource image, List<string> keywords)
    {
        var haystack = string.Join(" ", new[]
        {
            image.Id,
            image.Collection,
            image.ArticleKey,
            image.Title,
            image.Date,
            image.OriginalFileName,
            image.Alt,
            image.ContextBefore,
            image.ContextAfter,
            string.Join(" ", image.Tags)
        });

        var score = 0;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (image.Tags.Any(tag => string.Equals(tag, keyword, StringComparison.OrdinalIgnoreCase)))
                score += 8;

            score += Regex.Matches(haystack, Regex.Escape(keyword), RegexOptions.IgnoreCase).Count;
        }

        if (image.Collection == "illustrated")
            score += 1;

        return score;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static int? GetNullableInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        return value.TryGetInt32(out var result) ? result : null;
    }

    private static List<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
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
