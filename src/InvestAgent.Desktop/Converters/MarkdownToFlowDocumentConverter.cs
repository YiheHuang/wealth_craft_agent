using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InvestAgent.Desktop.Converters;

public class MarkdownToFlowDocumentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var markdown = value?.ToString() ?? "";
        return MarkdownFlowDocumentBuilder.Build(markdown);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

internal static class MarkdownFlowDocumentBuilder
{
    private const double DefaultDocumentWidth = 620;
    private const string ReasoningTagPattern = "think|thinking|analysis|reasoning|thought|thing";

    private static readonly Regex HeadingRegex = new(@"^\\?(#{1,6})\s*(\S.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^\d+[\.,、．]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BulletListRegex = new(@"^[-*]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex ImageRegex = new(@"^!\[(.*?)\]\((.+?)\)$", RegexOptions.Compiled);
    private static readonly Regex ImageShortcutRegex = new(@"^!([^\[\(].+)$", RegexOptions.Compiled);
    private static readonly Regex BlankHeadingRegex = new(@"^\\?[#＃\s]+$", RegexOptions.Compiled);
    private static readonly Regex HeadingWithBodyRegex = new(@"^(#{1,6})\s*(本轮追问|K线专项分析|缠论分析结合图示|本轮补充结论与风险|最终风险提示与投资建议|风险提示|简短结论|结论)([：:，,。.、\s-]*(.+))?$", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"(\*\*.+?\*\*|`.+?`|\[.+?\]\(.+?\))", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"^\[(.+?)\]\((.+?)\)$", RegexOptions.Compiled);
    private static readonly Lazy<List<ImageCandidate>> ChanImageIndex = new(LoadChanImageIndex);

    public static FlowDocument Build(string markdown)
    {
        markdown = NormalizeMarkdownForDisplay(markdown);
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            PagePadding = new Thickness(8, 0, 8, 0),
            TextAlignment = TextAlignment.Left,
            LineHeight = 23,
            PageWidth = DefaultDocumentWidth,
            MaxPageWidth = DefaultDocumentWidth,
            ColumnWidth = DefaultDocumentWidth
        };

        if (string.IsNullOrWhiteSpace(markdown))
            return doc;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            var headingMatch = HeadingRegex.Match(line.TrimStart());
            if (headingMatch.Success)
            {
                var headingText = CleanHeadingText(headingMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(headingText))
                    doc.Blocks.Add(BuildHeading(headingText, headingMatch.Groups[1].Value.Length));
                i++;
                continue;
            }

            var imageMatch = ImageRegex.Match(line.TrimStart());
            if (imageMatch.Success)
            {
                var imageBlock = BuildImageBlock(imageMatch.Groups[2].Value.Trim(), imageMatch.Groups[1].Value.Trim());
                doc.Blocks.Add(imageBlock ?? CreateParagraph(new[] { line.Trim() }, 0, 12));
                i++;
                continue;
            }

            var orderedMatch = OrderedListRegex.Match(line.TrimStart());
            var bulletMatch = BulletListRegex.Match(line.TrimStart());
            if (orderedMatch.Success || bulletMatch.Success)
            {
                var isOrdered = orderedMatch.Success;
                var list = new List
                {
                    MarkerStyle = isOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(28, 4, 0, 12),
                    Padding = new Thickness(0)
                };

                while (i < lines.Length)
                {
                    var current = lines[i].Trim();
                    var currentOrdered = OrderedListRegex.Match(current);
                    var currentBullet = BulletListRegex.Match(current);
                    if (string.IsNullOrWhiteSpace(current) || (!currentOrdered.Success && !currentBullet.Success))
                        break;

                    var content = currentOrdered.Success ? currentOrdered.Groups[1].Value : currentBullet.Groups[1].Value;
                    var paragraph = CreateParagraph(new[] { content }, 0, 0);
                    list.ListItems.Add(new ListItem(paragraph)
                    {
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                    i++;
                }

                doc.Blocks.Add(list);
                continue;
            }

            var paragraphLines = new List<string>();
            while (i < lines.Length)
            {
                var current = lines[i].TrimEnd();
                if (string.IsNullOrWhiteSpace(current))
                {
                    i++;
                    break;
                }

                if (HeadingRegex.IsMatch(current.TrimStart()) ||
                    ImageRegex.IsMatch(current.TrimStart()) ||
                    OrderedListRegex.IsMatch(current.TrimStart()) ||
                    BulletListRegex.IsMatch(current.TrimStart()))
                    break;

                paragraphLines.Add(current.Trim());
                i++;
            }

            if (paragraphLines.Count > 0)
                doc.Blocks.Add(CreateParagraph(paragraphLines, 0, 12));
        }

        return doc;
    }

    private static string NormalizeMarkdownForDisplay(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var normalized = StripReasoningArtifacts(markdown).Replace("\r\n", "\n").Replace('\r', '\n');
        var normalizedLines = new List<string>();
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            line = Regex.Replace(line, @"[\u200B-\u200D\uFEFF]", "");
            line = line.Replace('＃', '#');
            line = Regex.Replace(line, @"^(\\+)#", "#");
            line = Regex.Replace(line, @"^(\s*)[•·●‣]\s*", "$1- ");
            line = Regex.Replace(line, @"^(\s*)(#{1,6})(\S)", "$1$2 $3");
            line = Regex.Replace(line, @"^(\s*)(\d+)[、．]\s*", "$1$2. ");
            line = Regex.Replace(line, @"^(\s*)(\d+)\.([^\s])", "$1$2. $3");

            var trimmed = line.Trim();
            if (BlankHeadingRegex.IsMatch(trimmed))
                continue;

            var shortcut = ImageShortcutRegex.Match(trimmed);
            if (shortcut.Success)
            {
                var caption = shortcut.Groups[1].Value.Trim();
                normalizedLines.Add(BuildImageShortcutMarkdown(caption) ?? $"**图示：{caption}**");
                continue;
            }

            var splitHeading = SplitHeadingWithBody(trimmed);
            if (splitHeading is not null)
            {
                normalizedLines.AddRange(splitHeading);
                continue;
            }

            normalizedLines.Add(line);
        }

        normalized = string.Join("\n", normalizedLines);
        normalized = Regex.Replace(normalized, @"(?m)^(\s*#{1,6}\s+[^\n]+?)\s+(\d+\.\s+)", "$1\n$2");
        normalized = Regex.Replace(normalized, @"(?m)^(\s*#{1,6}\s+[^\n]+?)\s+(-\s+)", "$1\n$2");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string StripReasoningArtifacts(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, $@"(?is)<\s*(?:{ReasoningTagPattern})\b[^>]*>.*?<\s*/\s*(?:{ReasoningTagPattern})\s*>", "");
        text = Regex.Replace(text, $@"(?is)<\s*(?:{ReasoningTagPattern})\b[^>]*>.*$", "");

        var closingTags = Regex.Matches(text, $@"(?is)<\s*/\s*(?:{ReasoningTagPattern})\s*>");
        if (closingTags.Count > 0)
        {
            var last = closingTags[closingTags.Count - 1];
            var prefix = text[..last.Index];
            var suffix = text[(last.Index + last.Length)..];
            if (LooksLikeReasoning(prefix) || Regex.IsMatch(prefix, @"(?im)^\s*\d+\.\s+"))
                text = suffix;
            else
                text = Regex.Replace(text, $@"(?is)<\s*/\s*(?:{ReasoningTagPattern})\s*>", "");
        }

        text = RemoveLeadingReasoningSection(text);
        return text.Trim();
    }

    private static string RemoveLeadingReasoningSection(string text)
    {
        var marker = Regex.Match(
            text,
            @"(?im)^\s*(?:#{1,6}\s*)?(?:最终答案|正式回答|回答|正文|结论|公司主要业务|K线分析|新闻分析|财务分析|风险提示|投资建议|缠论视角|缠论分析)\s*[:：]?");

        if (marker.Success && marker.Index > 0)
        {
            var prefix = text[..marker.Index];
            if (LooksLikeReasoning(prefix))
                return text[marker.Index..];
        }

        return text;
    }

    private static bool LooksLikeReasoning(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               Regex.IsMatch(
                   text,
                   @"(?im)(思考过程|我的思考|推理过程|内部推理|先分析|我需要|用户要求|需要结合|当前数据|分析步骤|组织语言|直接回答用户问题|Chain\s*of\s*Thought|Thought\s*Process|Reasoning|We need|I need)");
    }

    private static List<string>? SplitHeadingWithBody(string line)
    {
        var match = HeadingWithBodyRegex.Match(line);
        if (!match.Success)
            return null;

        var level = Math.Clamp(match.Groups[1].Value.Length, 2, 3);
        var title = CleanHeadingText(match.Groups[2].Value);
        var body = match.Groups[4].Success ? match.Groups[4].Value.Trim() : "";
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var result = new List<string> { $"{new string('#', level)} {title}" };
        if (!string.IsNullOrWhiteSpace(body))
            result.Add(body);
        return result;
    }

    private static string CleanHeadingText(string text)
    {
        var cleaned = (text ?? "").Trim();
        cleaned = Regex.Replace(cleaned, @"[\u200B-\u200D\uFEFF]", "");
        cleaned = cleaned.Replace('＃', '#');
        cleaned = Regex.Replace(cleaned, @"^(?:\\?#\s*)+", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }

    private static string NormalizeMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var normalized = markdown.Replace("\r\n", "\n");
        normalized = Regex.Replace(normalized, @"(?<!\n)(#{2,6})", "\n$1");
        normalized = Regex.Replace(normalized, @"(?m)^(#{1,6})(\S)", "$1 $2");
        normalized = Regex.Replace(normalized, @"[•·]\s*", "- ");
        normalized = Regex.Replace(normalized, @"(?m)^(\d+)\.\s*", "$1. ");
        normalized = Regex.Replace(normalized, @"(?m)^(####?\s*[^\n#]+?)(\d+\.)", "$1\n$2");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static Paragraph BuildHeading(string text, int level)
    {
        var size = level switch
        {
            1 => 22,
            2 => 19,
            3 => 16,
            _ => 15
        };

        var p = new Paragraph
        {
            Margin = new Thickness(0, level <= 2 ? 8 : 5, 0, level <= 2 ? 10 : 7),
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"))
        };
        AddInlineContent(p.Inlines, text);
        return p;
    }

    private static Block? BuildImageBlock(string path, string altText)
    {
        try
        {
            var uri = ResolveImageUri(path);
            if ((uri is null || !File.Exists(uri.LocalPath)) && !string.IsNullOrWhiteSpace(altText))
            {
                var fallbackPath = ResolveImageShortcutPath(altText);
                if (!string.IsNullOrWhiteSpace(fallbackPath))
                    uri = new Uri(fallbackPath, UriKind.Absolute);
            }

            if (uri is null || !File.Exists(uri.LocalPath))
                return BuildImagePlaceholderBlock(altText, path);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 720;
            bitmap.EndInit();
            bitmap.Freeze();

            var panel = new StackPanel
            {
                Margin = new Thickness(0, 6, 0, 10)
            };
            panel.Children.Add(new Image
            {
                Source = bitmap,
                MaxWidth = 720,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            if (!string.IsNullOrWhiteSpace(altText))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = altText,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new BlockUIContainer(panel)
            {
                Margin = new Thickness(0, 4, 0, 12)
            };
        }
        catch
        {
            return null;
        }
    }

    private static Uri? ResolveImageUri(string path)
    {
        var cleaned = path.Trim().Trim('<', '>');
        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var parsed) && parsed.IsFile)
            return parsed;

        var normalized = cleaned.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(normalized))
            normalized = Path.GetFullPath(normalized);

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static Block BuildImagePlaceholderBlock(string altText, string path)
    {
        var caption = string.IsNullOrWhiteSpace(altText) ? "图片" : altText.Trim();
        var p = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 12),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
            FontSize = 13
        };
        p.Inlines.Add(new Bold(new Run($"图示：{caption}")));
        if (!string.IsNullOrWhiteSpace(path))
            p.Inlines.Add(new Run("（未找到本地图片）"));
        return p;
    }

    private static string? BuildImageShortcutMarkdown(string caption)
    {
        var path = ResolveImageShortcutPath(caption);
        return string.IsNullOrWhiteSpace(path) ? null : $"![{caption}]({path})";
    }

    private static string? ResolveImageShortcutPath(string caption)
    {
        var query = NormalizeImageSearchText(caption);
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var match = ChanImageIndex.Value
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreImageCandidate(candidate, query)
            })
            .Where(x => x.Score > 0 && File.Exists(x.Candidate.AbsolutePath))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.CollectionRank)
            .FirstOrDefault();

        return match?.Candidate.AbsolutePath;
    }

    private static int ScoreImageCandidate(ImageCandidate candidate, string query)
    {
        var score = 0;
        if (candidate.NormalizedFileName == query)
            score += 120;
        if (candidate.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 60 + query.Length;

        foreach (var token in BuildImageSearchTokens(query))
        {
            if (candidate.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += token.Length >= 3 ? 8 : 3;
        }

        return score;
    }

    private static IEnumerable<string> BuildImageSearchTokens(string query)
    {
        if (query.Length >= 2)
        {
            for (var i = 0; i <= query.Length - 2; i++)
                yield return query.Substring(i, 2);
        }
    }

    private static List<ImageCandidate> LoadChanImageIndex()
    {
        try
        {
            var manifestPath = ResolveChanImageManifestPath();
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return new List<ImageCandidate>();

            var manifestDir = new DirectoryInfo(Path.GetDirectoryName(manifestPath)!);
            var repoRoot = manifestDir.Parent?.Parent?.FullName ?? Directory.GetCurrentDirectory();
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
                return new List<ImageCandidate>();

            var result = new List<ImageCandidate>();
            foreach (var item in images.EnumerateArray())
            {
                var localPath = GetJsonString(item, "localPath");
                if (string.IsNullOrWhiteSpace(localPath))
                    continue;

                var normalizedLocalPath = localPath.Replace('/', Path.DirectorySeparatorChar);
                var absolutePath = Path.IsPathRooted(normalizedLocalPath)
                    ? Path.GetFullPath(normalizedLocalPath)
                    : Path.GetFullPath(Path.Combine(repoRoot, normalizedLocalPath));

                var title = GetJsonString(item, "title");
                var alt = GetJsonString(item, "alt");
                var originalFileName = GetJsonString(item, "originalFileName");
                var tags = GetJsonStringArray(item, "tags");
                var searchText = NormalizeImageSearchText(string.Join(" ", new[]
                {
                    title,
                    alt,
                    originalFileName,
                    GetJsonString(item, "contextBefore"),
                    GetJsonString(item, "contextAfter"),
                    string.Join(" ", tags)
                }));
                var collectionRank = normalizedLocalPath.Contains($"{Path.DirectorySeparatorChar}illustrated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1;

                result.Add(new ImageCandidate(
                    absolutePath,
                    searchText,
                    NormalizeImageSearchText(Path.GetFileNameWithoutExtension(originalFileName)),
                    collectionRank));
            }

            return result;
        }
        catch
        {
            return new List<ImageCandidate>();
        }
    }

    private static string? ResolveChanImageManifestPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "chan_images", "manifest.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static string NormalizeImageSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return Regex.Replace(text.ToLowerInvariant(), @"[\s\p{P}\p{S}_]+", "");
    }

    private static string GetJsonString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static List<string> GetJsonStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static Paragraph CreateParagraph(IEnumerable<string> lines, double top, double bottom)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, top, 0, bottom),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"))
        };

        var first = true;
        foreach (var line in lines)
        {
            if (!first)
                p.Inlines.Add(new LineBreak());
            AddInlineContent(p.Inlines, line);
            first = false;
        }
        return p;
    }

    private static void AddInlineContent(InlineCollection inlines, string text)
    {
        var lastIndex = 0;
        foreach (Match match in InlineRegex.Matches(text))
        {
            if (match.Index > lastIndex)
                inlines.Add(new Run(text[lastIndex..match.Index]));

            var token = match.Value;
            if (token.StartsWith("**") && token.EndsWith("**"))
            {
                inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith("`") && token.EndsWith("`"))
            {
                inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                });
            }
            else
            {
                var link = LinkRegex.Match(token);
                if (link.Success)
                {
                    var hyperlink = new Hyperlink(new Run(link.Groups[1].Value))
                    {
                        NavigateUri = Uri.TryCreate(link.Groups[2].Value, UriKind.Absolute, out var uri) ? uri : null
                    };
                    hyperlink.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E"));
                    hyperlink.RequestNavigate += (_, args) =>
                    {
                        try
                        {
                            if (args.Uri is not null)
                                Process.Start(new ProcessStartInfo(args.Uri.ToString()) { UseShellExecute = true });
                            args.Handled = true;
                        }
                        catch
                        {
                            // ignore
                        }
                    };
                    inlines.Add(hyperlink);
                }
                else
                {
                    inlines.Add(new Run(token));
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            inlines.Add(new Run(text[lastIndex..]));
    }

    private sealed record ImageCandidate(
        string AbsolutePath,
        string SearchText,
        string NormalizedFileName,
        int CollectionRank);
}
