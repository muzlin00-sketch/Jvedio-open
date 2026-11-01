using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jvedio.Core.MarkDown
{
    /// <summary>
    /// 极简 Markdown → <see cref="FlowDocument"/> 转换，取代
    /// <c>Jvedio.Core.MarkDown.MarkDown</c>。
    /// <para>
    /// 只服务公告弹窗，支持的是<b>行首语法</b>：<c>#</c> 标题、<c>&gt;</c> 引用、
    /// <c>[文字](链接)</c> 链接、<c>&lt;img src="..."&gt;</c> 图片，其余按普通段落。
    /// 原实现是逐行判断 + 字符串切割，这里保持同样的取舍，没有引入完整 Markdown 解析器。
    /// </para>
    /// </summary>
    public static class MarkDown
    {
        public const double DEFAULT_FONT_SIZE = 12;

        /// <summary>各级标题字号，索引即标题层级（最多 5 级）。</summary>
        public static List<double> FontSize { get; set; } = new List<double> { 28, 24, 20, 16, 14 };

        public static FlowDocument Parse(string str)
        {
            FlowDocument document = new FlowDocument();
            if (string.IsNullOrEmpty(str))
                return document;

            List<string> lines = str.Split(Environment.NewLine.ToCharArray()).ToList();
            foreach (string line in lines) {
                if (string.IsNullOrEmpty(line))
                    continue;

                int headerLevel = CountPrefix(line, '#');
                int quoteLevel = CountPrefix(line, '>');
                Paragraph paragraph = new Paragraph();

                if (headerLevel >= 0) {
                    string text = line.TrimStart('#').Trim();
                    paragraph.Inlines.Add(new Bold(new Run(text)));
                    int level = Math.Min(headerLevel, FontSize.Count - 1);
                    paragraph.FontSize = FontSize[level];
                    if (headerLevel <= 1) {
                        paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                        paragraph.BorderBrush = Brushes.Gray;
                    }

                    paragraph.Margin = new Thickness(0, 5, 0, 5);
                } else if (quoteLevel >= 0) {
                    string text = line.TrimStart('>').Trim();
                    paragraph.Inlines.Add(new Run(text));
                    paragraph.FontSize = DEFAULT_FONT_SIZE;
                    paragraph.BorderThickness = new Thickness(5, 0, 0, 0);
                    paragraph.BorderBrush = Brushes.Gray;
                    paragraph.Padding = new Thickness(5, 0, 0, 0);
                    paragraph.Margin = new Thickness(0, 3, 0, 3);
                } else {
                    paragraph.FontSize = DEFAULT_FONT_SIZE;
                    if (!TryAddLink(paragraph, line) && !TryAddImage(paragraph, line))
                        paragraph.Inlines.Add(new Run(line));
                }

                document.Blocks.Add(paragraph);
            }

            return document;
        }

        private static int CountPrefix(string line, char prefix)
        {
            int count = -1;
            foreach (char c in line) {
                if (c == prefix) {
                    count++;
                } else if (c == ' ') {
                    break;
                } else {
                    break;
                }
            }

            return count;
        }

        private static bool TryAddLink(Paragraph paragraph, string line)
        {
            int bracketStart = line.IndexOf('[');
            int bracketEnd = line.IndexOf("](");
            int close = line.IndexOf(')');
            if (!(bracketStart >= 0 && bracketStart < bracketEnd && bracketEnd < close))
                return false;

            if (bracketStart > 0)
                paragraph.Inlines.Add(new Run(line.Substring(0, bracketStart)));

            string text = line.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            string url = line.Substring(bracketEnd + 2, close - bracketEnd - 2);

            Underline link = new Underline(new Run(text));
            link.Foreground = Brushes.LightBlue;
            link.Cursor = Cursors.Hand;
            link.PreviewMouseLeftButtonDown += (sender, e) => {
                try {
                    Process.Start(url);
                } catch (Exception) {
                    // 打不开链接就算了，弹窗不该因为链接失败出错
                }
            };
            paragraph.Inlines.Add(link);

            if (close < line.Length - 1)
                paragraph.Inlines.Add(new Run(line.Substring(close + 1)));

            return true;
        }

        private static bool TryAddImage(Paragraph paragraph, string line)
        {
            Match match = Regex.Match(line, "<img.+?src=[\"'](.+?)[\"'].*?>", RegexOptions.IgnoreCase);
            string url = match.Groups[1].Value;
            if (string.IsNullOrEmpty(url))
                return false;

            float width = 0;
            float height = 0;
            string widthText = Regex.Match(line, "<img.+?width=[\"'](.+?)[\"'].*?>", RegexOptions.IgnoreCase).Groups[1].Value;
            string heightText = Regex.Match(line, "<img.+?height=[\"'](.+?)[\"'].*?>", RegexOptions.IgnoreCase).Groups[1].Value;
            float.TryParse(widthText, out width);
            float.TryParse(heightText, out height);

            System.Windows.Controls.Image image = new System.Windows.Controls.Image() {
                Source = new BitmapImage(new Uri(url)),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0),
            };
            if (width > 0)
                image.Width = width;

            if (height > 0)
                image.Height = height;

            paragraph.Inlines.Add(image);
            return true;
        }
    }
}
