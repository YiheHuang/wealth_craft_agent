namespace InvestAgent.Core.Models;

public class NewsItem
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishTime { get; set; }
    public string Sentiment { get; set; } = "neutral";
    public bool IsDataAvailable { get; set; } = true;
    public string DataNote { get; set; } = "";
}
