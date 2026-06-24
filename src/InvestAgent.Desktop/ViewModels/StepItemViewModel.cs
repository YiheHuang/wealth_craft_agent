namespace InvestAgent.Desktop.ViewModels;

/// <summary>
/// 分析步骤项的视图模型。
/// 用于在 Desktop UI 中展示单个分析步骤（Thought/Action/Observation/Response）。
/// </summary>
public class StepItemViewModel
{
    /// <summary>步骤标题（如 "Agent B | Action | get_historical_prices"）</summary>
    public string Title { get; set; } = "";

    /// <summary>步骤内容（工具返回的结果或响应文本）</summary>
    public string Content { get; set; } = "";
}
