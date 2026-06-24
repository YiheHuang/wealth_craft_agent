namespace InvestAgent.Core.Agent;

/// <summary>
/// Prompt 中的图片输入模型。
/// 包含图片的本地路径和 MIME 类型，用于多模态 LLM 调用。
/// </summary>
public class PromptImageInput
{
    /// <summary>图片唯一标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>本地文件绝对路径</summary>
    public string LocalPath { get; set; } = "";

    /// <summary>MIME 类型（如 image/png、image/jpeg）</summary>
    public string MimeType { get; set; } = "";
}
