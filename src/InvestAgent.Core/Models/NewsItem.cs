namespace InvestAgent.Core.Models;

/// <summary>
/// 新闻/公告条目数据模型。
/// 包含标题、摘要、来源、情绪标签等字段，支持公司新闻和行业新闻两种类型。
/// </summary>
public class NewsItem
{
    /// <summary>新闻标题</summary>
    public string Title { get; set; } = "";

    /// <summary>新闻摘要</summary>
    public string Summary { get; set; } = "";

    /// <summary>新闻正文内容</summary>
    public string Content { get; set; } = "";

    /// <summary>新闻原文链接</summary>
    public string Url { get; set; } = "";

    /// <summary>新闻来源（如 东方财富公告、Alpha Vantage 等）</summary>
    public string Source { get; set; } = "";

    /// <summary>发布时间</summary>
    public DateTime PublishTime { get; set; }

    /// <summary>情绪标签：positive(积极)、negative(消极)、neutral(中性)</summary>
    public string Sentiment { get; set; } = "neutral";

    /// <summary>数据是否可用</summary>
    public bool IsDataAvailable { get; set; } = true;

    /// <summary>数据备注说明</summary>
    public string DataNote { get; set; } = "";
}
