using System.Text.RegularExpressions;
using System.Text.Json;
using InvestAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAgent.Core.Services;

/// <summary>本地知识库服务接口</summary>
public interface ILocalKnowledgeService
{
    /// <summary>按主题和查询搜索本地知识文档</summary>
    /// <param name="topic">知识主题（目前支持 "chan"）</param>
    /// <param name="query">搜索查询</param>
    /// <param name="topN">返回最相关的前 N 条结果</param>
    List<string> Search(string topic, string query, int topN = 3);

    /// <summary>搜索缠论相关图片资源</summary>
    /// <param name="query">搜索关键词</param>
    /// <param name="topN">返回最大数量</param>
    List<ChanImageResource> SearchChanImages(string query, int topN = 6);

    /// <summary>获取缠论标准分析模板</summary>
    string GetChanAnalysisTemplate();
}

/// <summary>
/// 本地知识库服务的默认实现。
/// 从项目 docs/ 目录加载缠论文档和图片索引，
/// 支持基于关键词匹配的文档检索和图片搜索。
/// 数据在首次访问时延迟加载（Lazy）。
/// </summary>
public class LocalKnowledgeService : ILocalKnowledgeService
{
    private readonly ILogger<LocalKnowledgeService> _logger;
    private readonly string _docsRoot;

    /// <summary>缠论文档的延迟加载缓存</summary>
    private readonly Lazy<List<string>> _chanDocuments;

    /// <summary>缠论图片资源的延迟加载缓存</summary>
    private readonly Lazy<List<ChanImageResource>> _chanImages;

    public LocalKnowledgeService(ILogger<LocalKnowledgeService> logger)
    {
        _logger = logger;
        _docsRoot = ResolveDocsRoot();
        _chanDocuments = new Lazy<List<string>>(LoadChanDocuments);
        _chanImages = new Lazy<List<ChanImageResource>>(LoadChanImages);
    }

    /// <inheritdoc />
    public List<string> Search(string topic, string query, int topN = 3)
    {
        if (!string.Equals(topic, "chan", StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        var docs = _chanDocuments.Value;
        if (docs.Count == 0) return new List<string>();

        // 将文档拆分为 Markdown 二级标题段落（## 开头）
        var sections = docs
            .SelectMany(doc => Regex.Split(doc, @"(?=^##\s+)", RegexOptions.Multiline))
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 12)
            .ToList();

        // 提取搜索关键词
        var keywords = Regex.Matches(query ?? "", @"[一-龥A-Za-z0-9]+")
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 基于关键词命中次数排序
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

        return ranked.Count > 0 ? ranked : sections.Take(Math.Max(1, topN)).ToList();
    }

    /// <inheritdoc />
    public List<ChanImageResource> SearchChanImages(string query, int topN = 6)
    {
        var images = _chanImages.Value;
        if (images.Count == 0) return new List<ChanImageResource>();

        var keywords = BuildImageSearchKeywords(query);
        if (keywords.Count == 0) keywords.Add("chart-example");

        return images
            .Select(image => new { Image = image, Score = ScoreImage(image, keywords) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Image.Collection == "illustrated" ? 0 : 1)
            .ThenBy(x => x.Image.ArticleNo ?? int.MaxValue)
            .ThenBy(x => x.Image.ImageIndex)
            .Take(Math.Max(1, topN))
            .Select(x => x.Image)
            .ToList();
    }

    /// <inheritdoc />
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

    // ── 文档加载 ──────────────────────────────────────────

    /// <summary>从 docs/ 目录加载 chan_theory*.md 文件</summary>
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

            return files.Where(File.Exists).Select(File.ReadAllText)
                .Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载缠论知识库失败");
            return new List<string>();
        }
    }

    /// <summary>从 docs/chan_images/manifest.json 加载图片索引</summary>
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

    // ── 关键词提取与图片评分 ──────────────────────────────

    /// <summary>从查询文本中提取搜索关键词，并添加领域映射标签</summary>
    private static List<string> BuildImageSearchKeywords(string query)
    {
        var raw = query ?? "";
        var keywords = Regex.Matches(raw, @"[一-龥A-Za-z0-9\-]+")
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 1)
            .ToList();

        // 中文关键词 → 英文领域标签映射
        void AddIfContains(string keyword, string tag)
        {
            if (raw.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                keywords.Add(tag);
        }

        AddIfContains("图", "chart-example");
        AddIfContains("图解", "chart-example");
        AddIfContains("缠论", "chart-example");
        AddIfContains("缠中说禅", "chart-example");
        AddIfContains("分型", "fractal");
        AddIfContains("笔", "bi");
        AddIfContains("线段", "segment");
        AddIfContains("中枢", "zhongshu");
        AddIfContains("背驰", "divergence");
        AddIfContains("买点", "buy-sell-point");
        AddIfContains("卖点", "buy-sell-point");
        AddIfContains("区间套", "interval-nesting");
        AddIfContains("同级别", "same-level-decomposition");
        AddIfContains("走势", "trend-structure");
        AddIfContains("MACD", "macd");

        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>对图片进行关键词匹配评分</summary>
    private static int ScoreImage(ChanImageResource image, List<string> keywords)
    {
        // 构建全文搜索文本（所有元数据拼接）
        var haystack = string.Join(" ", new[]
        {
            image.Id, image.Collection, image.ArticleKey, image.Title,
            image.Date, image.OriginalFileName, image.Alt,
            image.ContextBefore, image.ContextAfter, string.Join(" ", image.Tags)
        });

        var score = 0;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;

            // 标签精确匹配加权
            if (image.Tags.Any(tag => string.Equals(tag, keyword, StringComparison.OrdinalIgnoreCase)))
                score += 8;

            // 全文模糊匹配
            score += Regex.Matches(haystack, Regex.Escape(keyword), RegexOptions.IgnoreCase).Count;
        }

        // illustrated 集合轻微加权
        if (image.Collection == "illustrated") score += 1;

        return score;
    }

    // ── JSON 辅助 ──────────────────────────────────────────

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    }

    private static int GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    }

    private static int? GetNullableInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
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

    /// <summary>解析 docs/ 目录的绝对路径（向上搜索项目根目录）</summary>
    private static string ResolveDocsRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir is not null)
        {
            var docs = Path.Combine(dir.FullName, "docs");
            if (Directory.Exists(docs)) return docs;
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "docs");
    }
}
