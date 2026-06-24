using System.ComponentModel;
using System.Text.Json;
using InvestAgent.Core.Services;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

/// <summary>
/// 本地知识库插件。
/// 为 LLM Agent 提供本地知识检索功能——包括缠论知识文档搜索和缠论图解资源检索。
/// 所有知识数据来自本地 docs/ 目录下的 Markdown 文件和图片索引。
/// </summary>
public class LocalKnowledgePlugin
{
    private readonly ILocalKnowledgeService _localKnowledgeService;

    public LocalKnowledgePlugin(ILocalKnowledgeService localKnowledgeService)
    {
        _localKnowledgeService = localKnowledgeService;
    }

    /// <summary>
    /// 搜索本地知识库。当前支持 "chan"（缠论）主题。
    /// 返回与查询最相关的文档片段。
    /// </summary>
    [KernelFunction("search_local_knowledge")]
    [Description("搜索本地知识库。topic 目前支持 chan。")]
    public string SearchLocalKnowledge(
        [Description("知识主题，例如 chan")] string topic,
        [Description("搜索问题或关键词")] string query,
        [Description("返回条数")] int topN = 3)
    {
        var results = _localKnowledgeService.Search(topic, query, topN);
        return string.Join("\n\n---\n\n", results);
    }

    /// <summary>
    /// 获取缠论标准分析模板。
    /// 包含级别确认、分型/笔/线段/中枢识别、背驰判断和买卖点分析的步骤指南。
    /// </summary>
    [KernelFunction("get_chan_analysis_template")]
    [Description("获取缠论标准分析模板。")]
    public string GetChanAnalysisTemplate() => _localKnowledgeService.GetChanAnalysisTemplate();

    /// <summary>
    /// 搜索缠论相关的图片示例。
    /// 返回匹配图片的元数据（localPath, pageUrl, imageUrl, tags, context 等）。
    /// </summary>
    [KernelFunction("search_chan_images")]
    [Description("Search local Chan theory image examples. Returns metadata including localPath, pageUrl, imageUrl, tags, and context.")]
    public string SearchChanImages(
        [Description("Query keywords, for example: zhongshu, fractal, bi, segment, divergence, buy point, MACD, image case, or Chinese equivalents.")]
        string query,
        [Description("Maximum number of images to return.")]
        int topN = 6)
    {
        var results = _localKnowledgeService.SearchChanImages(query, topN);
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }
}
