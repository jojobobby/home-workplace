using System.Text;

namespace HomeWorkplace.Office.Ui;

/// <summary>One text field. The first field is required; the rest may be empty.</summary>
public sealed record Field(string Name, bool Multiline, int MaxLength);

/// <summary>The values a text entry submitted, with the payload it was opened with.</summary>
public sealed record TextSubmitted(object? Payload, IReadOnlyList<string> Values);

/// <summary>
/// A small form: a title, one or more fields, a cursor. Enter moves down and submits on the
/// last field; Tab/Up/Down move between fields; Esc cancels. Text arrives as Char keys.
/// </summary>
public sealed class TextEntry : ILayer
{
    private readonly StringBuilder[] _values;

    /// <param name="initial">Text the fields start with (a rename shows the old name); clipped to each field's length, cursor at its end.</param>
    public TextEntry(string title, IReadOnlyList<Field> fields, object? payload, IReadOnlyList<string>? initial = null)
    {
        if (fields.Count == 0) throw new ArgumentException("a text entry needs at least one field", nameof(fields));
        Title = title;
        Fields = fields;
        Payload = payload;
        _values = fields.Select((f, i) =>
        {
            var text = initial is not null && i < initial.Count ? initial[i] : "";
            return new StringBuilder(text.Length > f.MaxLength ? text[..f.MaxLength] : text);
        }).ToArray();
        Cursor = _values[0].Length;
    }

    public string Title { get; }
    public IReadOnlyList<Field> Fields { get; }
    public object? Payload { get; }
    public int Current { get; private set; }
    public int Cursor { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<string> Values => _values.Select(v => v.ToString()).ToList();
    public string CurrentValue => _values[Current].ToString();

    public LayerResult Handle(UiKey key)
    {
        var text = _values[Current];
        switch (key.Kind)
        {
            case UiKeyKind.Char:
                if (char.IsControl(key.Ch)) break;
                if (text.Length >= Fields[Current].MaxLength) break;
                text.Insert(Cursor, key.Ch);
                Cursor++;
                Error = null;
                break;
            case UiKeyKind.Backspace:
                if (Cursor > 0) { text.Remove(Cursor - 1, 1); Cursor--; }
                break;
            case UiKeyKind.Delete:
                if (Cursor < text.Length) text.Remove(Cursor, 1);
                break;
            case UiKeyKind.Left:
                Cursor = Math.Max(0, Cursor - 1);
                break;
            case UiKeyKind.Right:
                Cursor = Math.Min(text.Length, Cursor + 1);
                break;
            case UiKeyKind.Tab:
            case UiKeyKind.Down:
                Select((Current + 1) % Fields.Count);
                break;
            case UiKeyKind.Up:
                Select((Current + Fields.Count - 1) % Fields.Count);
                break;
            case UiKeyKind.Accept:
                if (_values[0].Length == 0)
                {
                    Error = $"{Fields[0].Name} is required";
                    Select(0);
                    break;
                }
                if (Current < Fields.Count - 1) { Select(Current + 1); break; }
                return LayerResult.Submit(new TextSubmitted(Payload, Values));
            case UiKeyKind.Back:
                return LayerResult.Pop();
        }
        return LayerResult.None();
    }

    private void Select(int index)
    {
        Current = index;
        Cursor = _values[index].Length;
    }

    /// <summary>Greedy word wrap; words longer than a line are broken hard. Always at least one line.</summary>
    public static IReadOnlyList<string> Wrap(string text, int columns)
    {
        columns = Math.Max(1, columns);
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (w.Length > columns)
                {
                    if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                    lines.Add(w[..columns]);
                    w = w[columns..];
                }
                if (line.Length == 0) line.Append(w);
                else if (line.Length + 1 + w.Length <= columns) line.Append(' ').Append(w);
                else { lines.Add(line.ToString()); line.Clear().Append(w); }
            }
            lines.Add(line.ToString());
        }
        return lines;
    }
}
