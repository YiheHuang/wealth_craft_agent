namespace InvestAgent.Core.Models;

/// <summary>
/// 缠论图片资源元数据。
/// 包含图片的标识、路径、来源、标签和上下文信息，
/// 用于本地知识库中的缠论图解检索和引用。
/// </summary>
public class ChanImageResource
{
    /// <summary>图片唯一标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>所属集合（如 "illustrated"、"reference"）</summary>
    public string Collection { get; set; } = "";

    /// <summary>关联文章标识</summary>
    public string ArticleKey { get; set; } = "";

    /// <summary>关联文章编号</summary>
    public int? ArticleNo { get; set; }

    /// <summary>图片标题</summary>
    public string Title { get; set; } = "";

    /// <summary>图片日期</summary>
    public string Date { get; set; } = "";

    /// <summary>来源页面 URL</summary>
    public string PageUrl { get; set; } = "";

    /// <summary>图片在文章中的序号</summary>
    public int ImageIndex { get; set; }

    /// <summary>远程图片 URL</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>本地文件路径</summary>
    public string LocalPath { get; set; } = "";

    /// <summary>原始文件名</summary>
    public string OriginalFileName { get; set; } = "";

    /// <summary>图片替代文本</summary>
    public string Alt { get; set; } = "";

    /// <summary>图片前文上下文（用于理解图片在文章中的位置）</summary>
    public string ContextBefore { get; set; } = "";

    /// <summary>图片后文上下文</summary>
    public string ContextAfter { get; set; } = "";

    /// <summary>图片标签列表（用于关键词匹配）</summary>
    public List<string> Tags { get; set; } = new();
}
