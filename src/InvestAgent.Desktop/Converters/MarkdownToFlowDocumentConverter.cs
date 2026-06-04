using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

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
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^\d+\.\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BulletListRegex = new(@"^[-*]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"(\*\*.+?\*\*|`.+?`|\[.+?\]\(.+?\))", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"^\[(.+?)\]\((.+?)\)$", RegexOptions.Compiled);

    public static FlowDocument Build(string markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            LineHeight = 22
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
                doc.Blocks.Add(BuildHeading(headingMatch.Groups[2].Value.Trim(), headingMatch.Groups[1].Value.Length));
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
                    Margin = new Thickness(0, 4, 0, 10)
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
                    list.ListItems.Add(new ListItem(paragraph));
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

    private static Paragraph BuildHeading(string text, int level)
    {
        var size = level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            _ => 15
        };

        var p = new Paragraph
        {
            Margin = new Thickness(0, level <= 2 ? 6 : 3, 0, 10),
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"))
        };
        AddInlineContent(p.Inlines, text);
        return p;
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
}
