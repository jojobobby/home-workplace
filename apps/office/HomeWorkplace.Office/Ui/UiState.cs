namespace HomeWorkplace.Office.Ui;

public enum UiKeyKind { Up, Down, Left, Right, Accept, Back, Tab, Char, Backspace, Delete, PageUp, PageDown }

/// <summary>One logical key press, already mapped from the keyboard or the mouse.</summary>
public readonly record struct UiKey(UiKeyKind Kind, char Ch = '\0')
{
    public static readonly UiKey Up = new(UiKeyKind.Up);
    public static readonly UiKey Down = new(UiKeyKind.Down);
    public static readonly UiKey Left = new(UiKeyKind.Left);
    public static readonly UiKey Right = new(UiKeyKind.Right);
    public static readonly UiKey Accept = new(UiKeyKind.Accept);
    public static readonly UiKey Back = new(UiKeyKind.Back);
    public static readonly UiKey Tab = new(UiKeyKind.Tab);
    public static readonly UiKey Backspace = new(UiKeyKind.Backspace);
    public static readonly UiKey Delete = new(UiKeyKind.Delete);
    public static readonly UiKey PageUp = new(UiKeyKind.PageUp);
    public static readonly UiKey PageDown = new(UiKeyKind.PageDown);
    public static UiKey Char(char c) => new(UiKeyKind.Char, c);
}

public enum LayerResultKind { None, Pop, Push, Submit, Emit }

/// <summary>What a layer wants after a key: nothing, close itself, open another, hand back a payload and close (Submit), or hand one back and stay open (Emit).</summary>
public sealed record LayerResult(LayerResultKind Kind, ILayer? Layer = null, object? Payload = null)
{
    public static LayerResult None() => new(LayerResultKind.None);
    public static LayerResult Pop() => new(LayerResultKind.Pop);
    public static LayerResult Push(ILayer layer) => new(LayerResultKind.Push, layer);
    public static LayerResult Submit(object? payload) => new(LayerResultKind.Submit, Payload: payload);
    public static LayerResult Emit(object? payload) => new(LayerResultKind.Emit, Payload: payload);
}

/// <summary>A modal piece of UI: a dialogue, the overlay, a text entry, a confirm.</summary>
public interface ILayer
{
    LayerResult Handle(UiKey key);
}

/// <summary>
/// The layer stack. Keys go to the top layer only; a Pop or Submit result closes it; a Push
/// opens another on top. Pure state: the game routes input in and reads submits out.
/// </summary>
public sealed class UiState
{
    private readonly List<ILayer> _layers = new();

    public bool IsOpen => _layers.Count > 0;
    public ILayer? Top => _layers.Count == 0 ? null : _layers[^1];
    public IReadOnlyList<ILayer> Layers => _layers;

    public void Push(ILayer layer) => _layers.Add(layer);

    public void Pop()
    {
        if (_layers.Count > 0) _layers.RemoveAt(_layers.Count - 1);
    }

    public void Clear() => _layers.Clear();

    /// <summary>Route a key to the top layer and apply its result. Returns the result for the caller (submits carry payloads).</summary>
    public LayerResult Handle(UiKey key)
    {
        if (Top is not { } top) return LayerResult.None();
        var result = top.Handle(key);
        switch (result.Kind)
        {
            case LayerResultKind.Pop:
            case LayerResultKind.Submit:
                if (ReferenceEquals(Top, top)) Pop();
                break;
            case LayerResultKind.Push when result.Layer is not null:
                Push(result.Layer);
                break;
        }
        return result;
    }
}
