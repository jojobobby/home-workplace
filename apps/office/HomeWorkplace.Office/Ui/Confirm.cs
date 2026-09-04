namespace HomeWorkplace.Office.Ui;

/// <summary>A yes/no question for destructive actions. Yes submits the payload; No (or Esc) just closes.</summary>
public sealed class Confirm : ILayer
{
    public Confirm(string question, object? payload)
    {
        Question = question;
        Payload = payload;
    }

    public string Question { get; }
    public object? Payload { get; }
    /// <summary>0 = Yes, 1 = No.</summary>
    public int Selected { get; private set; }

    public LayerResult Handle(UiKey key) => key.Kind switch
    {
        UiKeyKind.Left or UiKeyKind.Up => Move(0),
        UiKeyKind.Right or UiKeyKind.Down or UiKeyKind.Tab => Move(1),
        UiKeyKind.Accept => Selected == 0 ? LayerResult.Submit(Payload) : LayerResult.Pop(),
        UiKeyKind.Back => LayerResult.Pop(),
        _ => LayerResult.None(),
    };

    private LayerResult Move(int to)
    {
        Selected = to;
        return LayerResult.None();
    }
}
