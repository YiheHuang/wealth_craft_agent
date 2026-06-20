namespace InvestAgent.Core.Models;

public class ChanImageResource
{
    public string Id { get; set; } = "";
    public string Collection { get; set; } = "";
    public string ArticleKey { get; set; } = "";
    public int? ArticleNo { get; set; }
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public int ImageIndex { get; set; }
    public string ImageUrl { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string Alt { get; set; } = "";
    public string ContextBefore { get; set; } = "";
    public string ContextAfter { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
