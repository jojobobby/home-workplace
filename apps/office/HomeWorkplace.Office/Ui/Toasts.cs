namespace HomeWorkplace.Office.Ui;

public enum ToastKind { Info, Success, Error, Attention }

public sealed class Toast
{
    public Toast(string text, ToastKind kind, string? employeeId)
    {
        Text = text;
        Kind = kind;
        EmployeeId = employeeId;
    }

    public string Text { get; }
    public ToastKind Kind { get; }
    /// <summary>When set, clicking the toast opens this employee's dialogue.</summary>
    public string? EmployeeId { get; }
    public float Age { get; internal set; }
}

/// <summary>Short notices stacked top-right: at most five, gone after a few seconds.</summary>
public sealed class Toasts
{
    public const int Max = 5;
    public const float Lifetime = 6f;

    private readonly List<Toast> _live = new();

    public IReadOnlyList<Toast> Live => _live;

    public Toast Add(string text, ToastKind kind, string? employeeId)
    {
        var toast = new Toast(text, kind, employeeId);
        _live.Add(toast);
        while (_live.Count > Max) _live.RemoveAt(0);
        return toast;
    }

    public void Update(float dt)
    {
        foreach (var t in _live) t.Age += dt;
        _live.RemoveAll(t => t.Age >= Lifetime);
    }

    public void Dismiss(Toast toast) => _live.Remove(toast);
}
