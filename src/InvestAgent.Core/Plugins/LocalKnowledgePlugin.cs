using System.ComponentModel;
using InvestAgent.Core.Services;
using Microsoft.SemanticKernel;

namespace InvestAgent.Core.Plugins;

public class LocalKnowledgePlugin
{
    private readonly ILocalKnowledgeService _localKnowledgeService;

    public LocalKnowledgePlugin(ILocalKnowledgeService localKnowledgeService)
    {
        _localKnowledgeService = localKnowledgeService;
    }

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

    [KernelFunction("get_chan_analysis_template")]
    [Description("获取缠论标准分析模板。")]
    public string GetChanAnalysisTemplate() => _localKnowledgeService.GetChanAnalysisTemplate();
}
