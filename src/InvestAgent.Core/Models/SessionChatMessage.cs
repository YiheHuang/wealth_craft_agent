namespace InvestAgent.Core.Models;

public class SessionChatMessage
{
    public long Id { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int TurnIndex { get; set; }
}
