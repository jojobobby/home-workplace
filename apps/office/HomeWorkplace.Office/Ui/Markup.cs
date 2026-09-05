using System.Text;

namespace HomeWorkplace.Office.Ui;

/// <summary>One styled stretch of a line: its text, an optional colour name, and whether it is small.</summary>
public sealed record Run(string Text, string? Color, bool Small);

/// <summary>
/// Tiny inline markup for UI text: <c>[gold]...[/]</c>, <c>[small]...[/]</c>,
/// <c>[small green]...[/]</c>. Colours: gold, green, red, blue, dim, white. Small runs use a
/// 4-px cell instead of 6, so details take less room and read as captions. Tags nest; <c>[/]</c>
/// closes the innermost. Anything that does not parse as a tag is plain text.
/// </summary>
public static class Markup
{
    public static readonly IReadOnlySet<string> Colors = new HashSet<string> { "gold", "green", "red", "blue", "dim", "white" };
    public const int Advance = 6;
    public const int SmallAdvance = 4;

    public static IReadOnlyList<Run> Parse(string text)
    {
        var runs = new List<Run>();
        var stack = new Stack<(string? Color, bool Small)>();
        var buffer = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[' && TryTag(text, i, out var tag, out var end))
            {
                Flush(runs, buffer, stack);
                if (tag == "/") { if (stack.Count > 0) stack.Pop(); }
                else
                {
                    var current = stack.Count > 0 ? stack.Peek() : (Color: (string?)null, Small: false);
                    foreach (var word in tag.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (word == "small") current.Small = true;
                        else if (Colors.Contains(word)) current.Color = word;
                    }
                    stack.Push(current);
                }
                i = end;
                continue;
            }
            buffer.Append(text[i]);
            i++;
        }
        Flush(runs, buffer, stack);
        return runs;
    }

    /// <summary>The text with every tag removed.</summary>
    public static string Strip(string text) => string.Concat(Parse(text).Select(r => r.Text));

    public static int VisibleLength(string text) => Parse(text).Sum(r => r.Text.Length);

    /// <summary>Width in native pixels: 6 per normal character, 4 per small one.</summary>
    public static int Measure(string text) => Parse(text).Sum(r => r.Text.Length * (r.Small ? SmallAdvance : Advance));

    /// <summary>The first <paramref name="visible"/> characters, tags kept and any tag cut open closed again.</summary>
    public static string Clip(string text, int visible)
    {
        if (VisibleLength(text) <= visible) return text;
        var head = Prefix(text, visible, out _);
        return head + string.Concat(Enumerable.Repeat("[/]", TagsOpenAfter(new List<string>(), head).Count));
    }

    /// <summary>
    /// Greedy word wrap by visible width in normal characters. Tags weigh nothing; a tag still
    /// open at a line break is closed on that line and re-opened on the next. A word longer than
    /// a line breaks hard.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int columns)
    {
        columns = Math.Max(1, columns);
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            var lineWidth = 0;
            var open = new List<string>();   // tags open at the end of the current line
            foreach (var rawWord in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = rawWord;
                var width = VisibleLength(word);
                while (width > columns)
                {
                    if (lineWidth > 0) { lines.Add(Close(line, open)); line.Clear(); Reopen(line, open); lineWidth = 0; }
                    var head = Prefix(word, columns, out var consumed);
                    line.Append(head);
                    open = TagsOpenAfter(open, head);
                    lines.Add(Close(line, open));
                    line.Clear();
                    Reopen(line, open);
                    word = word[consumed..];
                    width = VisibleLength(word);
                }
                if (lineWidth == 0) line.Append(word);
                else if (lineWidth + 1 + width <= columns) { line.Append(' ').Append(word); lineWidth += 1; }
                else { lines.Add(Close(line, open)); line.Clear(); Reopen(line, open); line.Append(word); lineWidth = 0; }
                lineWidth += width;
                open = TagsOpenAfter(open, word);
            }
            lines.Add(Close(line, open));
        }
        return lines;
    }

    /// <summary>The first <paramref name="visible"/> characters with the tags among them, left as they are; <paramref name="consumed"/> is how much of the source that covered.</summary>
    private static string Prefix(string text, int visible, out int consumed)
    {
        var sb = new StringBuilder();
        var left = Math.Max(0, visible);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[' && TryTag(text, i, out _, out var end)) { sb.Append(text, i, end - i); i = end; continue; }
            if (left == 0) break;
            sb.Append(text[i]); i++; left--;
        }
        consumed = i;
        return sb.ToString();
    }

    private static string Close(StringBuilder line, List<string> open)
        => open.Count == 0 ? line.ToString() : line + string.Concat(Enumerable.Repeat("[/]", open.Count));

    private static void Reopen(StringBuilder line, List<string> open)
    {
        foreach (var tag in open) line.Append('[').Append(tag).Append(']');
    }

    /// <summary>The tags still open after <paramref name="text"/>, given the ones open before it.</summary>
    private static List<string> TagsOpenAfter(List<string> before, string text)
    {
        var open = new List<string>(before);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[' && TryTag(text, i, out var tag, out var end))
            {
                if (tag == "/") { if (open.Count > 0) open.RemoveAt(open.Count - 1); }
                else open.Add(tag);
                i = end;
            }
            else i++;
        }
        return open;
    }

    private static bool TryTag(string text, int at, out string tag, out int end)
    {
        tag = "";
        end = at;
        var close = text.IndexOf(']', at + 1);
        if (close < 0 || close - at > 24) return false;
        var inner = text.Substring(at + 1, close - at - 1).Trim();
        if (inner.Length == 0) return false;
        if (inner != "/" && !inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => w == "small" || Colors.Contains(w))) return false;
        tag = inner;
        end = close + 1;
        return true;
    }

    private static void Flush(List<Run> runs, StringBuilder buffer, Stack<(string? Color, bool Small)> stack)
    {
        if (buffer.Length == 0) return;
        var style = stack.Count > 0 ? stack.Peek() : (Color: (string?)null, Small: false);
        runs.Add(new Run(buffer.ToString(), style.Color, style.Small));
        buffer.Clear();
    }
}
